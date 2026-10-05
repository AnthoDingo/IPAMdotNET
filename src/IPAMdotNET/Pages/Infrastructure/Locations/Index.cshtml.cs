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
        SectionAccess access = await SectionAccess.ForAsync(db, User);
        IQueryable<Subnet> readable = access.Readable(db.Subnets);
        IQueryable<Device> devices = access.Readable(db.Devices);
        Locations = await db.Locations
            .OrderBy(l => l.Name)
            .Select(l => new LocationRow(l,
                readable.Count(s => s.LocationId == l.Id),
                devices.Count(d => d.LocationId == l.Id),
                db.Racks.Count(r => r.LocationId == l.Id)))
            .ToListAsync();
    }
}
