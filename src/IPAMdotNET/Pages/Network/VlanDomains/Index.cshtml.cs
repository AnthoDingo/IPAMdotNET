using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Network.VlanDomains;

public sealed record VlanDomainRow(VlanDomain Domain, int VlanCount);

public class IndexModel(AppDbContext db) : PageModel
{
    public List<VlanDomainRow> Domains { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Domains = await db.VlanDomains
            .OrderBy(d => d.Name)
            .Select(d => new VlanDomainRow(d, db.Vlans.Count(v => v.DomainId == d.Id)))
            .ToListAsync();
    }
}
