using IPAMdotNet.Localization;
using System.Net;
using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using IPAMdotNet.Networking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Administration.Verify;

/// <summary>Résultat d'un contrôle : liste des anomalies (vide = OK), et handler de correction automatique s'il existe.</summary>
public sealed record CheckResult(string Title, string Description, List<string> Problems, string? FixHandler = null);

/// <summary>« Vérifier la base » de phpIPAM : état du schéma et contrôles de cohérence des données.</summary>
public class IndexModel(AppDbContext db) : PageModel
{
    public string? Provider { get; private set; }
    public List<string> AppliedMigrations { get; private set; } = [];
    public List<string> PendingMigrations { get; private set; } = [];
    public List<CheckResult> Checks { get; private set; } = [];

    [TempData]
    public string? Message { get; set; }

    public async Task OnGetAsync()
    {
        Provider = db.Database.ProviderName;
        AppliedMigrations = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        PendingMigrations = (await db.Database.GetPendingMigrationsAsync()).ToList();
        Checks =
        [
            await CheckSubnetsAsync(),
            await CheckAddressesAsync(),
            await CheckRackPositionsAsync(),
            await CheckPstnNumbersAsync(),
            await CheckRequestsAsync(),
            new CheckResult(L.T("Valeurs de champs personnalisés orphelines"),
                L.T("Valeurs dont l'objet porteur n'existe plus."),
                (await FindOrphansAsync()).Select(v => L.T("Valeur n°{0} (objet n°{1}) : « {2} »", v.Id, v.EntityId, v.Value)).ToList(),
                "DeleteOrphans"),
            await CheckCustomValuesAsync(),
        ];
    }

    public async Task<IActionResult> OnPostDeleteOrphansAsync()
    {
        List<CustomFieldValue> orphans = await FindOrphansAsync();
        db.CustomFieldValues.RemoveRange(orphans);
        await db.SaveChangesAsync();
        await db.LogAsync(LogSeverity.Info, LogEntry.Maintenance, $"Vérification de la base : {orphans.Count} valeur(s) orpheline(s) supprimée(s).",
            User.Identity?.Name, HttpContext.Connection.RemoteIpAddress?.ToString());
        Message = L.T("{0} valeur(s) orpheline(s) supprimée(s).", orphans.Count);
        return RedirectToPage();
    }

    /// <summary>Adresse sur 16 octets et réseau aligné sur son préfixe (bits d'hôte à zéro).</summary>
    private async Task<CheckResult> CheckSubnetsAsync()
    {
        List<Subnet> subnets = await db.Subnets.Include(s => s.Section).ToListAsync();
        List<string> problems = [];
        foreach (Subnet subnet in subnets)
        {
            if (subnet.Address.Length != 16)
            {
                problems.Add(L.T("Sous-réseau n°{0} ({1}) : adresse stockée invalide.", subnet.Id, subnet.Section?.Name));
                continue;
            }
            IPAddress address = Ip.FromBytes(subnet.Address);
            int maxPrefix = address.GetAddressBytes().Length * 8;
            if (subnet.PrefixLength < 0 || subnet.PrefixLength > maxPrefix || !Ip.TryParseNetwork($"{address}/{subnet.PrefixLength}", out _))
            {
                problems.Add(L.T("Sous-réseau n°{0} ({1}) : {2}/{3} n'est pas une adresse réseau valide.", subnet.Id, subnet.Section?.Name, address, subnet.PrefixLength));
            }
        }
        return new CheckResult(L.T("Sous-réseaux"), L.T("Adresses réseau valides et alignées sur leur préfixe."), problems);
    }

    /// <summary>Adresses comprises dans leur sous-réseau (un import ou une modification en base peut les en faire sortir).</summary>
    private async Task<CheckResult> CheckAddressesAsync()
    {
        List<IpAddress> addresses = await db.IpAddresses.Include(a => a.Subnet).ToListAsync();
        List<string> problems = [];
        foreach (IpAddress address in addresses.Where(a => a.Subnet is { Address.Length: 16 }))
        {
            if (address.Address.Length != 16)
            {
                problems.Add(L.T("Adresse n°{0} : valeur stockée invalide.", address.Id));
                continue;
            }
            IPNetwork network = address.Subnet!.Network;
            if (!Ip.Contains(network, new IPNetwork(address.Value, address.Value.GetAddressBytes().Length * 8)))
            {
                problems.Add(L.T("{0} : hors de son sous-réseau {1}.", address.Value, network));
            }
        }
        return new CheckResult(L.T("Adresses IP"), L.T("Adresses comprises dans leur sous-réseau."), problems);
    }

    private async Task<CheckResult> CheckRackPositionsAsync()
    {
        List<Device> devices = await db.Devices.Include(d => d.Rack).ToListAsync();
        List<string> problems = [];
        foreach (Device device in devices)
        {
            if (device.RackId is null && (device.RackStart is not null || device.RackSize is not null))
            {
                problems.Add(L.T("{0} : position renseignée sans rack.", device.Hostname));
            }
            else if (device.Rack is not null && (device.RackStart is null || device.RackSize is null))
            {
                problems.Add(L.T("{0} : dans le rack {1} sans position.", device.Hostname, device.Rack.Name));
            }
            else if (device.Rack is not null && device.RackFace == RackFace.Back && !device.Rack.HasBack)
            {
                problems.Add(L.T("{0} : en face arrière du rack {1}, qui n'en a pas.", device.Hostname, device.Rack.Name));
            }
            else if (device.Rack is not null && device.RackEnd > device.Rack.Size)
            {
                problems.Add(L.T("{0} : dépasse le rack {1} ({2} U).", device.Hostname, device.Rack.Name, device.Rack.Size));
            }
        }
        foreach (IGrouping<(int?, RackFace), Device> rack in devices.Where(d => d.RackId is not null && d.RackStart is not null && d.RackSize is not null).GroupBy(d => (d.RackId, d.RackFace)))
        {
            List<Device> placed = rack.OrderBy(d => d.RackStart).ToList();
            for (int i = 1; i < placed.Count; i++)
            {
                if (placed[i].RackStart <= placed[i - 1].RackEnd)
                {
                    problems.Add(L.T("{0} et {1} se chevauchent dans le rack {2}.", placed[i - 1].Hostname, placed[i].Hostname, placed[i].Rack?.Name));
                }
            }
        }
        return new CheckResult(L.T("Positions en rack"), L.T("Équipements positionnés dans leur rack, sans chevauchement."), problems);
    }

    private async Task<CheckResult> CheckPstnNumbersAsync()
    {
        List<string> problems = (await db.PstnNumbers.Include(n => n.Prefix)
                .Where(n => n.Number < n.Prefix!.Start || n.Number > n.Prefix!.Stop).ToListAsync())
            .Select(n => L.T("{0} {1} : hors de la plage {2} – {3}.", n.Prefix?.Prefix, n.Number, n.Prefix?.Start, n.Prefix?.Stop))
            .ToList();
        return new CheckResult(L.T("Numéros RTC"), L.T("Numéros compris dans la plage de leur préfixe."), problems);
    }

    private async Task<CheckResult> CheckRequestsAsync()
    {
        List<IpRequest> approved = await db.IpRequests.Include(r => r.Subnet)
            .Where(r => r.State == IpRequestState.Approved).ToListAsync();
        List<string> problems = [];
        foreach (IpRequest request in approved)
        {
            if (!IPAddress.TryParse(request.AssignedAddress, out IPAddress? address) || request.Subnet is null
                || request.Subnet.Address.Length != 16
                || !Ip.Contains(request.Subnet.Network, new IPNetwork(address, address.GetAddressBytes().Length * 8)))
            {
                problems.Add(L.T("Demande n°{0} : adresse attribuée « {1} » hors de son sous-réseau.", request.Id, request.AssignedAddress));
            }
        }
        problems.AddRange(approved.GroupBy(r => (r.SubnetId, r.AssignedAddress)).Where(g => g.Count() > 1)
            .Select(g => $"{g.Key.AssignedAddress} attribuée par {g.Count()} demandes ({string.Join(", ", g.Select(r => $"n°{r.Id}"))})."));
        return new CheckResult(L.T("Demandes d'adresses"), L.T("Adresses attribuées comprises dans leur sous-réseau et uniques."), problems);
    }

    /// <summary>Valeurs qui ne respectent plus le type du champ (type changé, choix de liste retiré…).</summary>
    private async Task<CheckResult> CheckCustomValuesAsync()
    {
        List<CustomFieldValue> values = await db.CustomFieldValues.Include(v => v.Field).ToListAsync();
        List<string> problems = values
            .Where(v => !CustomFieldForm.TryNormalize(v.Field!, v.Value, out string? normalized) || normalized != v.Value)
            .Select(v => L.T("{0} n°{1}, champ « {2} » : « {3} » ne correspond plus au type {4}.", L.T(ChangeLog.Types[v.Field!.EntityType].Label), v.EntityId, v.Field.Name, v.Value, v.Field.Type))
            .ToList();
        return new CheckResult(L.T("Valeurs de champs personnalisés"), L.T("Valeurs conformes au type actuel de leur champ (à corriger dans la fiche de l'objet)."), problems);
    }

    private async Task<List<CustomFieldValue>> FindOrphansAsync()
    {
        List<CustomFieldValue> values = await db.CustomFieldValues.Include(v => v.Field).ToListAsync();
        List<CustomFieldValue> orphans = [];
        foreach (IGrouping<string, CustomFieldValue> group in values.GroupBy(v => v.Field!.EntityType))
        {
            HashSet<int> ids = (await ExistingIdsAsync(group.Key)).ToHashSet();
            orphans.AddRange(group.Where(v => !ids.Contains(v.EntityId)));
        }
        return orphans;
    }

    private Task<List<int>> ExistingIdsAsync(string entityType) => entityType switch
    {
        nameof(Subnet) => db.Subnets.Select(x => x.Id).ToListAsync(),
        nameof(IpAddress) => db.IpAddresses.Select(x => x.Id).ToListAsync(),
        nameof(Vlan) => db.Vlans.Select(x => x.Id).ToListAsync(),
        nameof(Vrf) => db.Vrfs.Select(x => x.Id).ToListAsync(),
        nameof(Device) => db.Devices.Select(x => x.Id).ToListAsync(),
        nameof(Location) => db.Locations.Select(x => x.Id).ToListAsync(),
        nameof(Customer) => db.Customers.Select(x => x.Id).ToListAsync(),
        nameof(Rack) => db.Racks.Select(x => x.Id).ToListAsync(),
        nameof(Circuit) => db.Circuits.Select(x => x.Id).ToListAsync(),
        nameof(Section) => db.Sections.Select(x => x.Id).ToListAsync(),
        nameof(NatRule) => db.NatRules.Select(x => x.Id).ToListAsync(),
        nameof(BgpPeer) => db.BgpPeers.Select(x => x.Id).ToListAsync(),
        nameof(PstnPrefix) => db.PstnPrefixes.Select(x => x.Id).ToListAsync(),
        nameof(PstnNumber) => db.PstnNumbers.Select(x => x.Id).ToListAsync(),
        _ => Task.FromResult(new List<int>()),
    };
}
