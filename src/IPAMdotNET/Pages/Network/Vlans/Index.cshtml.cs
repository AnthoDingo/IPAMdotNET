using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Network.Vlans;

public sealed record VlanRow(Vlan Vlan, int SubnetCount);

public class IndexModel(AppDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true, Name = CustomFieldList.Prefix)]
    public CustomFieldList Custom { get; set; } = new();

    public List<VlanRow> Vlans { get; private set; } = [];
    public List<VlanDomain> Domains { get; private set; } = [];

    /// <summary>Domaine affiché ; null = tous.</summary>
    [BindProperty(SupportsGet = true)]
    public int? Domain { get; set; }

    public async Task OnGetAsync()
    {
        // Nombre de sous-réseaux limité aux sections lisibles.
        IQueryable<Subnet> readable = (await SectionAccess.ForAsync(db, User)).Readable(db.Subnets);
        await Custom.LoadAsync(db, nameof(Vlan));
        Domains = await db.VlanDomains.OrderBy(d => d.Name).ToListAsync();
        Vlans = await Custom.Apply(db, db.Vlans).Include(v => v.Domain)
            .Where(v => Domain == null || v.DomainId == Domain)
            .OrderBy(v => v.Number).ThenBy(v => v.Domain!.Name)
            .Select(v => new VlanRow(v, readable.Count(s => s.VlanId == v.Id)))
            .ToListAsync();
    }
}
