using IPAMdotNet.Data;
using IPAMdotNet.Networking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Network.Subnets;

public class DetailsModel(AppDbContext db) : PageModel
{
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
        return Page();
    }
}
