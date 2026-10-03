using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using IPAMdotNet.Navigation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Infrastructure.Locations;

public class DetailsModel(AppDbContext db) : PageModel
{
    public List<CustomFieldInput> CustomFieldValues { get; private set; } = [];

    public Location Location { get; private set; } = new();
    public LinkedObjects Linked { get; private set; } = new([], [], [], []);

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Location? location = await db.Locations.FindAsync(id);
        if (location is null)
        {
            return NotFound();
        }
        Location = location;
        Linked = new LinkedObjects(
            await db.Subnets.Where(s => s.LocationId == id).OrderBy(s => s.Address).ThenBy(s => s.PrefixLength).ToListAsync(),
            await db.Devices.Where(d => d.LocationId == id).OrderBy(d => d.Hostname).ToListAsync(),
            await db.Racks.Where(r => r.LocationId == id).OrderBy(r => r.Name).ToListAsync(),
            await db.Circuits.Include(c => c.Provider).Where(c => c.LocationAId == id || c.LocationBId == id).OrderBy(c => c.Cid).ToListAsync());
        CustomFieldValues = await CustomFieldForm.LoadAsync(db, nameof(Location), id);
        return Page();
    }
}
