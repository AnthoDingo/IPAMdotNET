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
            await CheckRackPositionsAsync(),
            await CheckPstnNumbersAsync(),
            await CheckRequestsAsync(),
            new CheckResult("Valeurs de champs personnalisés orphelines",
                "Valeurs dont l'objet porteur n'existe plus.",
                (await FindOrphansAsync()).Select(v => $"Valeur n°{v.Id} (objet n°{v.EntityId}) : « {v.Value} »").ToList(),
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
        Message = $"{orphans.Count} valeur(s) orpheline(s) supprimée(s).";
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
                problems.Add($"Sous-réseau n°{subnet.Id} ({subnet.Section?.Name}) : adresse stockée invalide.");
                continue;
            }
            IPAddress address = Ip.FromBytes(subnet.Address);
            int maxPrefix = address.GetAddressBytes().Length * 8;
            if (subnet.PrefixLength < 0 || subnet.PrefixLength > maxPrefix || !Ip.TryParseNetwork($"{address}/{subnet.PrefixLength}", out _))
            {
                problems.Add($"Sous-réseau n°{subnet.Id} ({subnet.Section?.Name}) : {address}/{subnet.PrefixLength} n'est pas une adresse réseau valide.");
            }
        }
        return new CheckResult("Sous-réseaux", "Adresses réseau valides et alignées sur leur préfixe.", problems);
    }

    private async Task<CheckResult> CheckRackPositionsAsync()
    {
        List<Device> devices = await db.Devices.Include(d => d.Rack).ToListAsync();
        List<string> problems = [];
        foreach (Device device in devices)
        {
            if (device.RackId is null && (device.RackStart is not null || device.RackSize is not null))
            {
                problems.Add($"{device.Hostname} : position renseignée sans rack.");
            }
            else if (device.Rack is not null && (device.RackStart is null || device.RackSize is null))
            {
                problems.Add($"{device.Hostname} : dans le rack {device.Rack.Name} sans position.");
            }
            else if (device.Rack is not null && device.RackEnd > device.Rack.Size)
            {
                problems.Add($"{device.Hostname} : dépasse le rack {device.Rack.Name} ({device.Rack.Size} U).");
            }
        }
        foreach (IGrouping<int?, Device> rack in devices.Where(d => d.RackId is not null && d.RackStart is not null && d.RackSize is not null).GroupBy(d => d.RackId))
        {
            List<Device> placed = rack.OrderBy(d => d.RackStart).ToList();
            for (int i = 1; i < placed.Count; i++)
            {
                if (placed[i].RackStart <= placed[i - 1].RackEnd)
                {
                    problems.Add($"{placed[i - 1].Hostname} et {placed[i].Hostname} se chevauchent dans le rack {placed[i].Rack?.Name}.");
                }
            }
        }
        return new CheckResult("Positions en rack", "Équipements positionnés dans leur rack, sans chevauchement.", problems);
    }

    private async Task<CheckResult> CheckPstnNumbersAsync()
    {
        List<string> problems = (await db.PstnNumbers.Include(n => n.Prefix)
                .Where(n => n.Number < n.Prefix!.Start || n.Number > n.Prefix!.Stop).ToListAsync())
            .Select(n => $"{n.Prefix?.Prefix} {n.Number} : hors de la plage {n.Prefix?.Start} – {n.Prefix?.Stop}.")
            .ToList();
        return new CheckResult("Numéros RTC", "Numéros compris dans la plage de leur préfixe.", problems);
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
                problems.Add($"Demande n°{request.Id} : adresse attribuée « {request.AssignedAddress} » hors de son sous-réseau.");
            }
        }
        problems.AddRange(approved.GroupBy(r => (r.SubnetId, r.AssignedAddress)).Where(g => g.Count() > 1)
            .Select(g => $"{g.Key.AssignedAddress} attribuée par {g.Count()} demandes ({string.Join(", ", g.Select(r => $"n°{r.Id}"))})."));
        return new CheckResult("Demandes d'adresses", "Adresses attribuées comprises dans leur sous-réseau et uniques.", problems);
    }

    /// <summary>Valeurs qui ne respectent plus le type du champ (type changé, choix de liste retiré…).</summary>
    private async Task<CheckResult> CheckCustomValuesAsync()
    {
        List<CustomFieldValue> values = await db.CustomFieldValues.Include(v => v.Field).ToListAsync();
        List<string> problems = values
            .Where(v => !CustomFieldForm.TryNormalize(v.Field!, v.Value, out string? normalized) || normalized != v.Value)
            .Select(v => $"{ChangeLog.Types[v.Field!.EntityType].Label} n°{v.EntityId}, champ « {v.Field.Name} » : « {v.Value} » ne correspond plus au type {v.Field.Type}.")
            .ToList();
        return new CheckResult("Valeurs de champs personnalisés", "Valeurs conformes au type actuel de leur champ (à corriger dans la fiche de l'objet).", problems);
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
        nameof(Vlan) => db.Vlans.Select(x => x.Id).ToListAsync(),
        nameof(Vrf) => db.Vrfs.Select(x => x.Id).ToListAsync(),
        nameof(Device) => db.Devices.Select(x => x.Id).ToListAsync(),
        nameof(Location) => db.Locations.Select(x => x.Id).ToListAsync(),
        nameof(Customer) => db.Customers.Select(x => x.Id).ToListAsync(),
        nameof(Rack) => db.Racks.Select(x => x.Id).ToListAsync(),
        nameof(Circuit) => db.Circuits.Select(x => x.Id).ToListAsync(),
        _ => Task.FromResult(new List<int>()),
    };
}
