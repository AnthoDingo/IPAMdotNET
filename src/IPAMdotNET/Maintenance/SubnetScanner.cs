using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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
/// Scan d'un sous-réseau par ping, puis ports TCP si configurés (agent de scan intégré, comme celui de phpIPAM) :
/// vérification de l'état des adresses connues et découverte des nouveaux hôtes.
/// </summary>
public static class SubnetScanner
{
    public const string LogCategory = "Scan";
    public const string AgentName = "Agent de scan";

    /// <summary>Au-delà (IPv4 /22), la découverte n'est pas tentée ; jamais en IPv6 (espace trop vaste).</summary>
    public const int MaxDiscoveryHosts = 1024;

    // Un seul scan à la fois dans le processus : évite qu'un scan manuel et l'agent créent la même adresse.
    private static readonly SemaphoreSlim Lock = new(1, 1);

    public static bool CanDiscover(Subnet subnet) => subnet.IsIPv4 && Ip.UsableCount(subnet.Network) <= MaxDiscoveryHosts;

    public static async Task<ScanReport> ScanAsync(AppDbContext db, int subnetId, ScanSettings settings, CancellationToken cancellationToken)
    {
        await Lock.WaitAsync(cancellationToken);
        try
        {
            return await ScanLockedAsync(db, subnetId, settings, cancellationToken);
        }
        finally
        {
            Lock.Release();
        }
    }

    private static async Task<ScanReport> ScanLockedAsync(AppDbContext db, int subnetId, ScanSettings settings, CancellationToken cancellationToken)
    {
        Subnet subnet = await db.Subnets.AsNoTracking().SingleAsync(s => s.Id == subnetId, cancellationToken);
        List<IpAddress> addresses = await db.IpAddresses.Include(a => a.Tag).Where(a => a.SubnetId == subnetId).ToListAsync(cancellationToken);
        Tag? used = await db.Tags.SingleOrDefaultAsync(t => t.SystemKey == Tag.UsedKey, cancellationToken);
        Tag? offline = await db.Tags.SingleOrDefaultAsync(t => t.SystemKey == Tag.OfflineKey, cancellationToken);

        List<IpAddress> toPing = subnet.PingCheck ? addresses.Where(a => !a.ExcludePing).ToList() : [];
        List<IPAddress> toDiscover = [];
        if (subnet.Discover && CanDiscover(subnet))
        {
            HashSet<BigInteger> known = addresses.Select(a => Ip.ToNumber(a.Value)).ToHashSet();
            (BigInteger first, BigInteger last) = Ip.UsableRange(subnet.Network);
            for (BigInteger candidate = first; candidate <= last; candidate++)
            {
                if (!known.Contains(candidate))
                {
                    toDiscover.Add(Ip.FromNumber(candidate, ipv4: true));
                }
            }
        }

        HashSet<IPAddress> online = await ProbeAllAsync(toPing.Select(a => a.Value).Concat(toDiscover), settings, cancellationToken);
        DateTime now = DateTime.UtcNow;

        // « Vu le » : mise à jour en masse, volontairement hors journal des modifications (elle change à chaque scan).
        List<int> seen = toPing.Where(a => online.Contains(a.Value)).Select(a => a.Id).ToList();
        if (seen.Count > 0)
        {
            await db.IpAddresses.Where(a => seen.Contains(a.Id)).ExecuteUpdateAsync(s => s.SetProperty(a => a.LastSeen, now), cancellationToken);
        }

        // Étiquettes « mise à jour par le scan » : en ligne → Utilisée ; hors ligne → Hors ligne, seulement après
        // deux intervalles sans réponse (un ping perdu ne fait pas basculer l'adresse).
        DateTime offlineAfter = now.AddMinutes(-2 * settings.IntervalMinutes);
        int tagChanges = 0;
        foreach (IpAddress address in toPing.Where(a => a.Tag?.UpdateByScan == true))
        {
            bool isOnline = online.Contains(address.Value);
            Tag? target = isOnline ? used : (address.LastSeen is null || address.LastSeen < offlineAfter ? offline : null);
            if (target is not null && address.TagId != target.Id)
            {
                address.TagId = target.Id;
                tagChanges++;
            }
        }

        List<IPAddress> discovered = toDiscover.Where(online.Contains).ToList();
        foreach (IPAddress address in discovered)
        {
            db.IpAddresses.Add(new IpAddress
            {
                SubnetId = subnetId,
                Address = Ip.ToBytes(address),
                Hostname = settings.ResolveHostnames ? await ResolveAsync(address) : null,
                Description = "Découverte par le scan",
                TagId = used?.Id,
                LastSeen = now,
            });
        }

        db.AuditUserId = null;
        db.AuditUserName = AgentName;
        await db.SaveChangesAsync(cancellationToken);
        await db.Subnets.Where(s => s.Id == subnetId).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastScanAt, now), cancellationToken);
        return new ScanReport(toPing.Count, seen.Count, discovered.Count, tagChanges);
    }

    /// <summary>Adresses qui répondent au ping ou, à défaut, sur l'un des ports TCP configurés.</summary>
    private static async Task<HashSet<IPAddress>> ProbeAllAsync(IEnumerable<IPAddress> addresses, ScanSettings settings, CancellationToken cancellationToken)
    {
        using SemaphoreSlim slots = new(settings.Parallelism);
        TimeSpan timeout = TimeSpan.FromMilliseconds(settings.TimeoutMilliseconds);
        int[] ports = settings.TcpPortList;
        IEnumerable<Task<IPAddress?>> probes = addresses.Select(async address =>
        {
            await slots.WaitAsync(cancellationToken);
            try
            {
                if (await PingAsync(address, timeout, cancellationToken))
                {
                    return address;
                }
                // Ports essayés en parallèle : un hôte éteint coûte un seul délai, pas un par port.
                bool[] open = await Task.WhenAll(ports.Select(port => TcpAsync(address, port, timeout, cancellationToken)));
                return open.Any(o => o) ? address : null;
            }
            finally
            {
                slots.Release();
            }
        });
        IPAddress?[] results = await Task.WhenAll(probes);
        return results.OfType<IPAddress>().ToHashSet();
    }

    private static async Task<bool> PingAsync(IPAddress address, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            // Une instance de Ping par envoi : la classe n'accepte pas d'envois simultanés.
            using Ping ping = new();
            PingReply reply = await ping.SendPingAsync(address, timeout, cancellationToken: cancellationToken);
            return reply.Status == IPStatus.Success;
        }
        catch (PingException)
        {
            return false;
        }
    }

    /// <summary>
    /// Connexion TCP : acceptée ou refusée (RST), l'hôte est présent ; sans réponse dans le délai, il est considéré absent.
    /// </summary>
    public static async Task<bool> TcpAsync(IPAddress address, int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        using Socket socket = new(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(address, port, limit.Token);
            return true;
        }
        catch (SocketException e)
        {
            return e.SocketErrorCode == SocketError.ConnectionRefused;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>Nom DNS inverse, avec un délai court ; null si aucun nom.</summary>
    private static async Task<string?> ResolveAsync(IPAddress address)
    {
        try
        {
            Task<IPHostEntry> lookup = Dns.GetHostEntryAsync(address);
            if (await Task.WhenAny(lookup, Task.Delay(2000)) != lookup)
            {
                return null;
            }
            string name = (await lookup).HostName;
            return name == address.ToString() || string.IsNullOrEmpty(name) ? null : name.Length > 100 ? name[..100] : name;
        }
        catch (SocketException)
        {
            return null;
        }
    }
}
