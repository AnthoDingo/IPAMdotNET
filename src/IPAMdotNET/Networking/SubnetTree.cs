using System.Net;
using IPAMdotNet.Data;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Networking;

/// <param name="Used">Nombre d'adresses IP enregistrées dans ce sous-réseau (hors sous-réseaux enfants).</param>
public sealed record SubnetNode(Subnet Subnet, int Depth, int? ParentId, int Used = 0);

public sealed record SectionSubnets(Section Section, List<SubnetNode> Nodes);

/// <summary>Colonne gauche des pages section / sous-réseau : arbre de la section, sous-réseau courant surligné.</summary>
public sealed record SubnetSidebar(Section Section, List<SubnetNode> Nodes, int? SelectedId);

/// <summary>
/// Hiérarchie des sous-réseaux d'une section. Deux blocs CIDR sont soit disjoints soit imbriqués :
/// le parent est donc toujours le plus petit sous-réseau qui contient, et n'a pas besoin d'être stocké.
/// </summary>
public static class SubnetTree
{
    /// <param name="sorted">Sous-réseaux triés par adresse puis par longueur de préfixe croissante.</param>
    public static List<SubnetNode> Build(IEnumerable<Subnet> sorted, IReadOnlyDictionary<int, int>? usage = null)
    {
        List<SubnetNode> nodes = [];
        Stack<Subnet> ancestors = new();
        foreach (Subnet subnet in sorted)
        {
            IPNetwork network = subnet.Network;
            while (ancestors.Count > 0 && !Ip.Contains(ancestors.Peek().Network, network))
            {
                ancestors.Pop();
            }
            nodes.Add(new SubnetNode(subnet, ancestors.Count, ancestors.Count > 0 ? ancestors.Peek().Id : null, usage?.GetValueOrDefault(subnet.Id) ?? 0));
            ancestors.Push(subnet);
        }
        return nodes;
    }

    /// <summary>Arbre des sous-réseaux d'une section.</summary>
    public static async Task<List<SubnetNode>> LoadAsync(AppDbContext db, int sectionId) =>
        Build(await Sorted(db.Subnets.Where(s => s.SectionId == sectionId)).ToListAsync(),
            await UsageAsync(db.IpAddresses.Where(a => a.Subnet!.SectionId == sectionId)));

    /// <summary>Arbres de toutes les sections ayant au moins un sous-réseau retenu par <paramref name="filter"/>.</summary>
    public static async Task<List<SectionSubnets>> LoadAllAsync(AppDbContext db, Func<Subnet, bool>? filter = null)
    {
        // ponytail: tout est chargé en mémoire puis filtré ; paginer par section si les volumes deviennent importants.
        List<Subnet> subnets = await Sorted(db.Subnets.Include(s => s.Section)).ToListAsync();
        Dictionary<int, int> usage = await UsageAsync(db.IpAddresses);
        return subnets
            .Where(s => filter is null || filter(s))
            .GroupBy(s => s.Section!)
            .OrderBy(g => g.Key.Name)
            .Select(g => new SectionSubnets(g.Key, Build(g, usage)))
            .ToList();
    }

    /// <summary>Nombre d'adresses par sous-réseau.</summary>
    public static Task<Dictionary<int, int>> UsageAsync(IQueryable<IpAddress> addresses) =>
        addresses.GroupBy(a => a.SubnetId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);

    // Tri binaire sur les 16 octets : identique sur SQL Server, PostgreSQL et MySQL.
    private static IQueryable<Subnet> Sorted(IQueryable<Subnet> query) =>
        query.Include(s => s.Vlan).Include(s => s.Vrf)
            .OrderBy(s => s.Address).ThenBy(s => s.PrefixLength);
}
