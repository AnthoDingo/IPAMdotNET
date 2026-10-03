using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Infrastructure.Racks;

public sealed record RackRow(Rack Rack, int DeviceCount, int UsedUnits);

public class IndexModel(AppDbContext db) : PageModel
{
    public List<RackRow> Racks { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Racks = await db.Racks
            .Include(r => r.Location).Include(r => r.Customer)
            .OrderBy(r => r.Name)
            .Select(r => new RackRow(r, r.Devices.Count, r.Devices.Sum(d => d.RackSize ?? 0)))
            .ToListAsync();
    }
}
