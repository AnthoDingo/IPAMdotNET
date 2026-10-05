using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Infrastructure.Racks;

public class DetailsModel(AppDbContext db) : PageModel
{
    public List<CustomFieldInput> CustomFieldValues { get; private set; } = [];

    public Rack Rack { get; private set; } = new();

    /// <summary>Occupant de chaque unité, index = numéro d'unité (l'index 0 est inutilisé).</summary>
    public Device?[] Units { get; private set; } = [];

    public List<Device> Unplaced { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Rack? rack = await db.Racks
            .Include(r => r.Location).Include(r => r.Customer)
            .Include(r => r.Devices).ThenInclude(d => d.DeviceType)
            .Include(r => r.Devices).ThenInclude(d => d.Sections)
            .SingleOrDefaultAsync(r => r.Id == id);
        if (rack is null)
        {
            return NotFound();
        }
        Rack = rack;
        Units = new Device?[rack.Size + 1];
        SectionAccess access = await SectionAccess.ForAsync(db, User);
        // Un équipement non visible occupe quand même ses unités : affiché sans ses informations.
        foreach (Device device in rack.Devices.Select(d => access.CanSee(d) ? d
            : new Device { Hostname = "Équipement masqué", RackStart = d.RackStart, RackSize = d.RackSize }))
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
        CustomFieldValues = await CustomFieldForm.LoadAsync(db, nameof(Rack), id);
        return Page();
    }
}
