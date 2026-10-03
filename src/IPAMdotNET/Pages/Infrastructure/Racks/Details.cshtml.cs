using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Infrastructure.Racks;

public class DetailsModel(AppDbContext db) : PageModel
{
    public Rack Rack { get; private set; } = new();

    /// <summary>Occupant de chaque unité, index = numéro d'unité (l'index 0 est inutilisé).</summary>
    public Device?[] Units { get; private set; } = [];

    public List<Device> Unplaced { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Rack? rack = await db.Racks
            .Include(r => r.Location).Include(r => r.Customer)
            .Include(r => r.Devices).ThenInclude(d => d.DeviceType)
            .SingleOrDefaultAsync(r => r.Id == id);
        if (rack is null)
        {
            return NotFound();
        }
        Rack = rack;
        Units = new Device?[rack.Size + 1];
        foreach (Device device in rack.Devices)
        {
            if (device.RackStart is null || device.RackEnd is null)
            {
                Unplaced.Add(device);
                continue;
            }
            for (int unit = device.RackStart.Value; unit <= Math.Min(device.RackEnd.Value, rack.Size); unit++)
            {
                Units[unit] = device;
            }
        }
        return Page();
    }
}
