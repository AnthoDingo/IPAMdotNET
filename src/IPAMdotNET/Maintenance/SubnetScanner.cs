using System.Net;
using System.Numerics;
using IPAMdotNet.Data;
using IPAMdotNet.Networking;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Maintenance;

public sealed record ScanReport(int Pinged, int Online, int Discovered, int TagChanges)
{
    public override string ToString() =>
        $"{Pinged} adresse(s) testée(s), {Online} en ligne, {Discovered} découverte(s), {TagChanges} étiquette(s) mise(s) à jour";
}

/// <summary>
/// Scan d'un sous-réseau (comme les agents de phpIPAM) : vérification de l'état des adresses connues et découverte
/// des nouveaux hôtes. En deux temps : <see cref="TargetsAsync"/> (adresses à sonder), puis <see cref="ApplyAsync"/>
/// (résultats). L'agent intégré enchaîne les deux (<see cref="ScanAsync"/>) ; un agent distant sonde entre les deux.
/// </summary>
public static class SubnetScanner
{
    public const string LogCategory = "Scan";
    public const string AgentName = "Agent de scan";

    /// <summary>Au-delà (IPv4 /22), la découverte n'est pas tentée ; jamais en IPv6 (espace trop vaste).</summary>
    public const int MaxDiscoveryHosts = 1024;

    // Un seul enregistrement de résultats à la fois dans le processus : évite que deux scans créent la même adresse.
    private static readonly SemaphoreSlim Lock = new(1, 1);

    public static bool CanDiscover(Subnet subnet) => subnet.IsIPv4 && Ip.UsableCount(subnet.Network) <= MaxDiscoveryHosts;

    /// <summary>Adresses à sonder : connues non exclues (vérification d'état) et libres attribuables (découverte).</summary>
    public static async Task<(List<IPAddress> Check, List<IPAddress> Discover)> TargetsAsync(AppDbContext db, Subnet subnet, CancellationToken cancellationToken)
    {
        List<IpAddress> addresses = await db.IpAddresses.AsNoTracking().Where(a => a.SubnetId == subnet.Id).ToListAsync(cancellationToken);
        List<IPAddress> check = subnet.PingCheck ? addresses.Where(a => !a.ExcludePing).Select(a => a.Value).ToList() : [];
        List<IPAddress> discover = [];
        if (subnet.Discover && CanDiscover(subnet))
        {
            HashSet<BigInteger> known = addresses.Select(a => Ip.ToNumber(a.Value)).ToHashSet();
            (BigInteger first, BigInteger last) = Ip.UsableRange(subnet.Network);
            for (BigInteger candidate = first; candidate <= last; candidate++)
            {
                if (!known.Contains(candidate))
                {
                    discover.Add(Ip.FromNumber(candidate, ipv4: true));
                }
            }
        }
        return (check, discover);
    }

    /// <summary>Scan complet par l'agent intégré, depuis le serveur.</summary>
    public static async Task<ScanReport> ScanAsync(AppDbContext db, int subnetId, ScanSettings settings, CancellationToken cancellationToken)
    {
        Subnet subnet = await db.Subnets.AsNoTracking().SingleAsync(s => s.Id == subnetId, cancellationToken);
        (List<IPAddress> check, List<IPAddress> discover) = await TargetsAsync(db, subnet, cancellationToken);
        List<IPAddress> targets = [.. check, .. discover];
        HashSet<IPAddress> online = await HostProbe.ProbeAllAsync(targets, TimeSpan.FromMilliseconds(settings.TimeoutMilliseconds),
            settings.Parallelism, settings.TcpPortList, cancellationToken);
        return await ApplyAsync(db, subnetId, targets.ToHashSet(), online, null, null, settings, cancellationToken);
    }

    /// <summary>
    /// Enregistre un scan : « vu le », étiquettes, hôtes découverts, date du dernier scan. Seules les adresses de
    /// <paramref name="probed"/> sont prises en compte (une adresse ajoutée pendant un scan distant n'est pas déclarée hors ligne).
    /// </summary>
    /// <param name="hostnames">Noms résolus par un agent distant ; null = résolution locale si activée.</param>
    /// <param name="macs">MAC relevées par un agent distant ; null = table ARP locale.</param>
    public static async Task<ScanReport> ApplyAsync(AppDbContext db, int subnetId, IReadOnlySet<IPAddress> probed, IReadOnlySet<IPAddress> online,
        IReadOnlyDictionary<IPAddress, string>? hostnames, IReadOnlyDictionary<IPAddress, string>? macs, ScanSettings settings,
        CancellationToken cancellationToken)
    {
        await Lock.WaitAsync(cancellationToken);
        try
        {
            Subnet subnet = await db.Subnets.AsNoTracking().SingleAsync(s => s.Id == subnetId, cancellationToken);
            List<IpAddress> addresses = await db.IpAddresses.Include(a => a.Tag).Where(a => a.SubnetId == subnetId).ToListAsync(cancellationToken);
            Tag? used = await db.Tags.SingleOrDefaultAsync(t => t.SystemKey == Tag.UsedKey, cancellationToken);
            Tag? offline = await db.Tags.SingleOrDefaultAsync(t => t.SystemKey == Tag.OfflineKey, cancellationToken);

            List<IpAddress> checkedAddresses = subnet.PingCheck ? addresses.Where(a => !a.ExcludePing && probed.Contains(a.Value)).ToList() : [];
            DateTime now = DateTime.UtcNow;

            // « Vu le » : mise à jour en masse, volontairement hors journal des modifications (elle change à chaque scan).
            List<int> seen = checkedAddresses.Where(a => online.Contains(a.Value)).Select(a => a.Id).ToList();
            if (seen.Count > 0)
            {
                await db.IpAddresses.Where(a => seen.Contains(a.Id)).ExecuteUpdateAsync(s => s.SetProperty(a => a.LastSeen, now), cancellationToken);
            }

            // Étiquettes « mise à jour par le scan » : en ligne → Utilisée ; hors ligne → Hors ligne, seulement après
            // deux intervalles sans réponse (un ping perdu ne fait pas basculer l'adresse).
            DateTime offlineAfter = now.AddMinutes(-2 * settings.IntervalMinutes);
            int tagChanges = 0;
            foreach (IpAddress address in checkedAddresses.Where(a => a.Tag?.UpdateByScan == true))
            {
                bool isOnline = online.Contains(address.Value);
                Tag? target = isOnline ? used : (address.LastSeen is null || address.LastSeen < offlineAfter ? offline : null);
                if (target is not null && address.TagId != target.Id)
                {
                    address.TagId = target.Id;
                    tagChanges++;
                }
            }

            List<IPAddress> discovered = [];
            if (subnet.Discover && CanDiscover(subnet))
            {
                HashSet<BigInteger> known = addresses.Select(a => Ip.ToNumber(a.Value)).ToHashSet();
                (BigInteger first, BigInteger last) = Ip.UsableRange(subnet.Network);
                discovered = online.Where(probed.Contains)
                    .Where(ip => Ip.ToNumber(ip) is BigInteger n && n >= first && n <= last && !known.Contains(n)).ToList();
            }
            // Nom d'hôte et MAC des adresses en ligne : renseignés seulement s'ils sont vides (une saisie n'est jamais écrasée).
            List<IpAddress> onlineKnown = checkedAddresses.Where(a => online.Contains(a.Value)).ToList();
            hostnames ??= settings.ResolveHostnames
                ? await HostProbe.ResolveAllAsync([.. onlineKnown.Where(a => string.IsNullOrEmpty(a.Hostname)).Select(a => a.Value), .. discovered])
                : new Dictionary<IPAddress, string>();
            macs ??= await HostProbe.ReadArpTableAsync(cancellationToken);
            foreach (IpAddress address in onlineKnown)
            {
                if (string.IsNullOrEmpty(address.Hostname) && Hostname(hostnames, address.Value) is { } name)
                {
                    address.Hostname = name;
                }
                if (string.IsNullOrEmpty(address.MacAddress) && IpAddress.NormalizeMac(macs.GetValueOrDefault(address.Value)) is { } mac)
                {
                    address.MacAddress = mac;
                }
            }

            foreach (IPAddress address in discovered)
            {
                db.IpAddresses.Add(new IpAddress
                {
                    SubnetId = subnetId,
                    Address = Ip.ToBytes(address),
                    Hostname = Hostname(hostnames, address),
                    MacAddress = IpAddress.NormalizeMac(macs.GetValueOrDefault(address)),
                    Description = "Découverte par le scan",
                    TagId = used?.Id,
                    LastSeen = now,
                });
            }

            db.AuditUserId = null;
            db.AuditUserName = AgentName;
            await db.SaveChangesAsync(cancellationToken);
            await db.Subnets.Where(s => s.Id == subnetId).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastScanAt, now), cancellationToken);
            return new ScanReport(checkedAddresses.Count, seen.Count, discovered.Count, tagChanges);
        }
        finally
        {
            Lock.Release();
        }
    }

    private static string? Hostname(IReadOnlyDictionary<IPAddress, string> hostnames, IPAddress address) =>
        hostnames.GetValueOrDefault(address)?.Trim() is { Length: > 0 } name ? (name.Length > 100 ? name[..100] : name) : null;
}
