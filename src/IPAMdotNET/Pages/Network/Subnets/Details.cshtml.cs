using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using IPAMdotNet.Navigation;
using IPAMdotNet.Networking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Network.Subnets;

/// <summary>Ligne de la liste des adresses : une adresse, ou une plage regroupée (<see cref="Count"/> &gt; 1).</summary>
public sealed record AddressRow(IpAddress First, IpAddress Last, int Count);

public class DetailsModel(AppDbContext db) : PageModel
{
    public List<CustomFieldInput> CustomFieldValues { get; private set; } = [];

    public Subnet Subnet { get; private set; } = new();

    /// <summary>Arbre complet de la section (colonne gauche).</summary>
    public List<SubnetNode> Tree { get; private set; } = [];

    /// <summary>Du sous-réseau racine jusqu'au parent direct.</summary>
    public List<Subnet> Ancestors { get; private set; } = [];

    public List<SubnetNode> Descendants { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Subnet? subnet = await db.Subnets
            .Include(s => s.Section).Include(s => s.Vlan).Include(s => s.Vrf).Include(s => s.Nameserver)
            .Include(s => s.Location).Include(s => s.Customer)
            .SingleOrDefaultAsync(s => s.Id == id);
        if (subnet is null)
        {
            return NotFound();
        }
        SectionAccess access = await SectionAccess.ForAsync(db, User);
        if (!access.CanRead(subnet.SectionId))
        {
            return Forbid();
        }
        CanWrite = access.CanWrite(subnet.SectionId);
        Subnet = subnet;
        Tree = await SubnetTree.LoadAsync(db, subnet.SectionId);

        Dictionary<int, SubnetNode> byId = Tree.ToDictionary(n => n.Subnet.Id);
        for (int? parentId = byId[id].ParentId; parentId is not null; parentId = byId[parentId.Value].ParentId)
        {
            Ancestors.Insert(0, byId[parentId.Value].Subnet);
        }

        // Les descendants suivent immédiatement le nœud dans l'arbre, tant que la profondeur est supérieure.
        int index = Tree.FindIndex(n => n.Subnet.Id == id);
        int depth = Tree[index].Depth;
        Descendants = Tree.Skip(index + 1).TakeWhile(n => n.Depth > depth).ToList();

        int userId = User.UserId();
        IsFavorite = await db.FavoriteSubnets.AnyAsync(f => f.UserId == userId && f.SubnetId == id);
        CustomFieldValues = await CustomFieldForm.LoadAsync(db, nameof(Subnet), id);

        List<IpAddress> addresses = await db.IpAddresses.Include(a => a.Tag).Include(a => a.Device)
            .Where(a => a.SubnetId == id).OrderBy(a => a.Address).ToListAsync();
        AddressCount = addresses.Count;
        AddressRows = Compress(addresses);
        FirstFree = Ip.FirstFree(subnet.Network, addresses.Select(a => Ip.ToNumber(a.Value)).ToHashSet());
        ScanEnabled = (await SettingsStore.LoadAsync<ScanSettings>(db, SettingsStore.ScanPrefix)).Enabled;
        return Page();
    }

    public bool IsFavorite { get; private set; }

    public bool CanWrite { get; private set; }

    public List<AddressRow> AddressRows { get; private set; } = [];
    public int AddressCount { get; private set; }
    public System.Net.IPAddress? FirstFree { get; private set; }
    public bool ScanEnabled { get; private set; }

    [TempData]
    public string? Message { get; set; }

    /// <summary>« Scanner maintenant » : scan immédiat de ce sous-réseau par l'agent intégré.</summary>
    public async Task<IActionResult> OnPostScanAsync(int id, CancellationToken cancellationToken)
    {
        Subnet? subnet = await db.Subnets.FindAsync(id);
        if (subnet is null)
        {
            return NotFound();
        }
        if (!(await SectionAccess.ForAsync(db, User)).CanWrite(subnet.SectionId))
        {
            return Forbid();
        }
        ScanSettings settings = await SettingsStore.LoadAsync<ScanSettings>(db, SettingsStore.ScanPrefix);
        if (!settings.Enabled)
        {
            Message = "L'agent de scan est désactivé (Administration › Agents de scan).";
            return RedirectToPage(new { id });
        }
        ScanReport report = await SubnetScanner.ScanAsync(db, id, settings, cancellationToken);
        Message = $"Scan terminé : {report}.";
        return RedirectToPage(new { id });
    }

    /// <summary>Regroupe les adresses consécutives portant une même étiquette « regrouper les plages » (ex. DHCP).</summary>
    private static List<AddressRow> Compress(List<IpAddress> addresses)
    {
        List<AddressRow> rows = [];
        foreach (IpAddress address in addresses)
        {
            AddressRow? previous = rows.Count > 0 ? rows[^1] : null;
            if (previous is not null && address.Tag?.Compress == true && previous.First.TagId == address.TagId
                && Ip.ToNumber(address.Value) == Ip.ToNumber(previous.Last.Value) + 1)
            {
                rows[^1] = previous with { Last = address, Count = previous.Count + 1 };
            }
            else
            {
                rows.Add(new AddressRow(address, address, 1));
            }
        }
        return rows;
    }

    public async Task<IActionResult> OnPostToggleFavoriteAsync(int id)
    {
        int userId = User.UserId();
        int removed = await db.FavoriteSubnets.Where(f => f.UserId == userId && f.SubnetId == id).ExecuteDeleteAsync();
        if (removed == 0 && await db.Subnets.AnyAsync(s => s.Id == id))
        {
            db.FavoriteSubnets.Add(new FavoriteSubnet { UserId = userId, SubnetId = id });
            await db.SaveChangesAsync();
        }
        return RedirectToPage(new { id });
    }
}
