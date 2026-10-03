using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Network.Vlans;

public sealed record VlanRow(Vlan Vlan, int SubnetCount);

public class IndexModel(AppDbContext db) : PageModel
{
    public List<VlanRow> Vlans { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Vlans = await db.Vlans
            .OrderBy(v => v.Number)
            .Select(v => new VlanRow(v, db.Subnets.Count(s => s.VlanId == v.Id)))
            .ToListAsync();
    }
}
