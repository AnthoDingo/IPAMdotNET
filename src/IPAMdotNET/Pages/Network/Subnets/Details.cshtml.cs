using System.Numerics;
using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using IPAMdotNet.Navigation;
using IPAMdotNet.Networking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Network.Subnets;

/// <summary>Ligne de la liste des adresses : une adresse, ou une plage regroupée (<see cref="Count"/> &gt; 1).</summary>
public sealed record AddressRow(IpAddress First, IpAddress Last, int Count)
{
    /// <summary>Adresses de la ligne (sélection pour les actions en masse).</summary>
    public List<int> Ids { get; init; } = [First.Id];
}

/// <summary>Plage d'adresses attribuables libres, intercalée dans la liste comme dans phpIPAM.</summary>
public sealed record FreeRange(System.Net.IPAddress First, System.Net.IPAddress Last, BigInteger Count);

/// <summary>Case de l'affichage visuel : une adresse du sous-réseau, son entrée éventuelle, attribuable ou non (réseau, diffusion).</summary>
public sealed record GridCell(System.Net.IPAddress Address, IpAddress? Entry, bool Usable);

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
            .Include(s => s.Location).Include(s => s.Customer).Include(s => s.ScanAgent)
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
        AddressFields = await CustomFieldForm.DefinitionsAsync(db, nameof(IpAddress));
        if (AddressFields.Count > 0)
        {
            AddressValues = (await db.CustomFieldValues
                    .Where(v => v.Field!.EntityType == nameof(IpAddress) && db.IpAddresses.Any(a => a.Id == v.EntityId && a.SubnetId == id))
                    .ToListAsync())
                .GroupBy(v => v.EntityId).ToDictionary(g => g.Key, g => g.ToDictionary(v => v.FieldId, v => v.Value));
        }
        AddressFilter.Fields = AddressFields;
        List<AddressRow> rows = Compress(await AddressFilter.ApplyAsync<IpAddress, IpAddress>(db, addresses, a => a.Id));
        Rows = SettingsStore.Server.HideFreeRanges || AddressFilter.Active ? [.. rows] : WithFreeRanges(subnet.Network, rows);
        if (subnet.IsIPv4 && Ip.AddressCount(subnet.Network) <= GridMaxAddresses)
        {
            Grid = BuildGrid(subnet.Network, addresses);
        }
        FirstFree = Ip.FirstFree(subnet.Network, addresses.Select(a => Ip.ToNumber(a.Value)).ToHashSet());
        ScanEnabled = subnet.ScanAgent?.Enabled ?? (await SettingsStore.LoadAsync<ScanSettings>(db, SettingsStore.ScanPrefix)).Enabled;
        NatRules = await db.NatRules.Include(n => n.Objects).ThenInclude(o => o.Subnet).Include(n => n.Objects).ThenInclude(o => o.Address)
            .Where(n => n.Objects.Any(o => o.SubnetId == id || (o.Address != null && o.Address.SubnetId == id)))
            .OrderBy(n => n.Name).ToListAsync();
        BgpPeers = await db.BgpPeerSubnets.Include(x => x.BgpPeer).Where(x => x.SubnetId == id)
            .OrderBy(x => x.Direction).ThenBy(x => x.BgpPeer!.Name).ToListAsync();
        return Page();
    }

    public bool IsFavorite { get; private set; }

    public bool CanWrite { get; private set; }

    /// <summary>Lignes de la liste, dans l'ordre des adresses : <see cref="AddressRow"/> ou <see cref="FreeRange"/>.</summary>
    public List<object> Rows { get; private set; } = [];

    /// <summary>Affichage visuel, limité aux sous-réseaux IPv4 de <see cref="GridMaxAddresses"/> adresses au plus (/22).</summary>
    public List<GridCell> Grid { get; private set; } = [];
    public const int GridMaxAddresses = 1024;

    /// <summary>Légende de l'affichage visuel : étiquettes présentes (null = adresse sans étiquette).</summary>
    public IEnumerable<Tag?> GridTags => Grid.Select(c => c.Entry).OfType<IpAddress>().Select(a => a.Tag).DistinctBy(t => t?.Id).OrderBy(t => t?.Name);
    public int AddressCount { get; private set; }

    /// <summary>Champs personnalisés des adresses (colonnes de la liste) et leurs valeurs : adresse → (champ → valeur).</summary>
    public List<CustomField> AddressFields { get; private set; } = [];

    /// <summary>Filtre de la liste des adresses sur un champ personnalisé (sans plages libres).</summary>
    [BindProperty(SupportsGet = true, Name = CustomFieldList.Prefix)]
    public CustomFieldList AddressFilter { get; set; } = new();
    public Dictionary<int, Dictionary<int, string>> AddressValues { get; private set; } = [];
    public System.Net.IPAddress? FirstFree { get; private set; }
    public bool ScanEnabled { get; private set; }

    /// <summary>Règles NAT liées au sous-réseau ou à l'une de ses adresses.</summary>
    public List<NatRule> NatRules { get; private set; } = [];

    /// <summary>Pairs BGP auxquels le sous-réseau est annoncé ou dont il est reçu.</summary>
    public List<BgpPeerSubnet> BgpPeers { get; private set; } = [];

    [TempData]
    public string? Message { get; set; }

    /// <summary>« Scanner maintenant » : rend le sous-réseau dû et réveille l'agent intégré (scan en arrière-plan).</summary>
    public async Task<IActionResult> OnPostScanAsync(int id)
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
        if (subnet.ScanAgentId is not null)
        {
            // Agent distant : on ne peut pas le joindre, mais on rend le sous-réseau dû pour son prochain passage (chaque minute).
            await db.Subnets.Where(s => s.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastScanAt, (DateTime?)null));
            Message = "Sous-réseau confié à un agent distant : il sera scanné à son prochain passage (dans la minute s'il est actif).";
            return RedirectToPage(new { id });
        }
        ScanSettings settings = await SettingsStore.LoadAsync<ScanSettings>(db, SettingsStore.ScanPrefix);
        if (!settings.Enabled)
        {
            Message = "L'agent de scan est désactivé (Administration › Agents de scan).";
            return RedirectToPage(new { id });
        }
        await db.Subnets.Where(s => s.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastScanAt, (DateTime?)null));
        ScanAgent.Wake(force: false);
        Message = "Scan lancé en arrière-plan : rechargez la page dans quelques instants pour voir le résultat.";
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
                previous.Ids.Add(address.Id);
                rows[^1] = previous with { Last = address, Count = previous.Count + 1 };
            }
            else
            {
                rows.Add(new AddressRow(address, address, 1));
            }
        }
        return rows;
    }

    /// <summary>Intercale les plages libres de la plage attribuable entre les lignes d'adresses.</summary>
    private static List<object> WithFreeRanges(System.Net.IPNetwork network, List<AddressRow> rows)
    {
        (BigInteger next, BigInteger last) = Ip.UsableRange(network);
        bool ipv4 = network.BaseAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
        List<object> result = [];
        foreach (AddressRow row in rows)
        {
            BigInteger start = Ip.ToNumber(row.First.Value);
            if (start > next)
            {
                result.Add(new FreeRange(Ip.FromNumber(next, ipv4), Ip.FromNumber(start - 1, ipv4), start - next));
            }
            result.Add(row);
            next = BigInteger.Max(next, Ip.ToNumber(row.Last.Value) + 1);
        }
        if (next <= last)
        {
            result.Add(new FreeRange(Ip.FromNumber(next, ipv4), Ip.FromNumber(last, ipv4), last - next + 1));
        }
        return result;
    }

    private static List<GridCell> BuildGrid(System.Net.IPNetwork network, List<IpAddress> addresses)
    {
        Dictionary<BigInteger, IpAddress> byNumber = addresses.ToDictionary(a => Ip.ToNumber(a.Value));
        (BigInteger first, BigInteger last) = Ip.UsableRange(network);
        BigInteger end = Ip.ToNumber(Ip.LastAddress(network));
        List<GridCell> cells = [];
        for (BigInteger n = Ip.ToNumber(network.BaseAddress); n <= end; n++)
        {
            cells.Add(new GridCell(Ip.FromNumber(n, ipv4: true), byNumber.GetValueOrDefault(n), n >= first && n <= last));
        }
        return cells;
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
