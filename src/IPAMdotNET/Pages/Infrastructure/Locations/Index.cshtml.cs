using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Infrastructure.Locations;

public sealed record LocationRow(Location Location, int SubnetCount, int DeviceCount, int RackCount);

public class IndexModel(AppDbContext db) : PageModel
{
    public List<LocationRow> Locations { get; private set; } = [];

    public async Task OnGetAsync()
    {
        // Nombre de sous-réseaux limité aux sections lisibles.
        IQueryable<Subnet> readable = (await SectionAccess.ForAsync(db, User)).Readable(db.Subnets);
        Locations = await db.Locations
            .OrderBy(l => l.Name)
            .Select(l => new LocationRow(l,
                readable.Count(s => s.LocationId == l.Id),
                db.Devices.Count(d => d.LocationId == l.Id),
                db.Racks.Count(r => r.LocationId == l.Id)))
            .ToListAsync();
    }
}
