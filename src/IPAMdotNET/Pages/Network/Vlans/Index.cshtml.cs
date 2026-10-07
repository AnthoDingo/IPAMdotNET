using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Network.Vlans;

public sealed record VlanRow(Vlan Vlan, int SubnetCount);

public class IndexModel(AppDbContext db) : PageModel
{
    public List<CustomField> CustomFields { get; private set; } = [];
    public Dictionary<int, Dictionary<int, string>> CustomValues { get; private set; } = [];

    public List<VlanRow> Vlans { get; private set; } = [];
    public List<VlanDomain> Domains { get; private set; } = [];

    /// <summary>Domaine affiché ; null = tous.</summary>
    [BindProperty(SupportsGet = true)]
    public int? Domain { get; set; }

    public async Task OnGetAsync()
    {
        // Nombre de sous-réseaux limité aux sections lisibles.
        IQueryable<Subnet> readable = (await SectionAccess.ForAsync(db, User)).Readable(db.Subnets);
        CustomFields = await CustomFieldForm.DefinitionsAsync(db, nameof(Vlan));
        CustomValues = await CustomFieldForm.ValuesForAsync(db, nameof(Vlan));
        Domains = await db.VlanDomains.OrderBy(d => d.Name).ToListAsync();
        Vlans = await db.Vlans.Include(v => v.Domain)
            .Where(v => Domain == null || v.DomainId == Domain)
            .OrderBy(v => v.Number).ThenBy(v => v.Domain!.Name)
            .Select(v => new VlanRow(v, readable.Count(s => s.VlanId == v.Id)))
            .ToListAsync();
    }
}
