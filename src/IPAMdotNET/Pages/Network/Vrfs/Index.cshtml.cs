using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Network.Vrfs;

public sealed record VrfRow(Vrf Vrf, int SubnetCount);

public class IndexModel(AppDbContext db) : PageModel
{
    public List<VrfRow> Vrfs { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Vrfs = await db.Vrfs
            .OrderBy(v => v.Name)
            .Select(v => new VrfRow(v, db.Subnets.Count(s => s.VrfId == v.Id)))
            .ToListAsync();
    }
}
