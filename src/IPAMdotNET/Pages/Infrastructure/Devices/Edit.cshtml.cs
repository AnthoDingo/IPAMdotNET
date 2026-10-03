using System.Net;
using IPAMdotNet.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IPAMdotNet.Pages.Infrastructure.Devices;

[Authorize(Policy = "Admin")]
public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public Device Device { get; set; } = new();

    public List<SelectListItem> Types { get; private set; } = [];
    public List<SelectListItem> Locations { get; private set; } = [];
    public List<SelectListItem> Customers { get; private set; } = [];
    public List<SelectListItem> Racks { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is not null)
        {
            Device? device = await db.Devices.FindAsync(id);
            if (device is null)
            {
                return NotFound();
            }
            Device = device;
        }
        await LoadListsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Device.Id = id ?? 0;

        if (!string.IsNullOrWhiteSpace(Device.IpAddress))
        {
            if (IPAddress.TryParse(Device.IpAddress.Trim(), out IPAddress? address))
            {
                Device.IpAddress = address.ToString();
            }
            else
            {
                ModelState.AddModelError("Device.IpAddress", "Adresse IP invalide.");
            }
        }
        else
        {
            Device.IpAddress = null;
        }

        await ValidateRackPositionAsync();

        if (!ModelState.IsValid)
        {
            await LoadListsAsync();
            return Page();
        }
        db.Update(Device);
        await db.SaveChangesAsync();
        return RedirectToPage("Index", null, $"device-{Device.Id}");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        Device? device = await db.Devices.FindAsync(id);
        if (device is null)
        {
            return NotFound();
        }
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync();
        await db.DetachDeviceAsync(id);
        db.Devices.Remove(device);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return RedirectToPage("Index");
    }

    /// <summary>Position obligatoire dans un rack, contenue dans sa hauteur et sans chevauchement avec un autre équipement.</summary>
    private async Task ValidateRackPositionAsync()
    {
        if (Device.RackId is null)
        {
            Device.RackStart = null;
            Device.RackSize = null;
            return;
        }
        if (Device.RackStart is null || Device.RackSize is null)
        {
            ModelState.AddModelError("Device.RackStart", "Position et hauteur requises pour placer l'équipement dans un rack.");
            return;
        }
        Rack? rack = await db.Racks.FindAsync(Device.RackId);
        if (rack is null)
        {
            ModelState.AddModelError("Device.RackId", "Rack inconnu.");
            return;
        }
        int start = Device.RackStart.Value;
        int end = start + Device.RackSize.Value - 1;
        if (end > rack.Size)
        {
            ModelState.AddModelError("Device.RackStart", $"L'équipement dépasse du rack ({rack.Size} U).");
            return;
        }
        Device? overlap = await db.Devices
            .Where(d => d.RackId == rack.Id && d.Id != Device.Id && d.RackStart != null
                && d.RackStart <= end && start <= d.RackStart + d.RackSize - 1)
            .FirstOrDefaultAsync();
        if (overlap is not null)
        {
            ModelState.AddModelError("Device.RackStart", $"Ces unités sont déjà occupées par {overlap.Hostname}.");
        }
    }

    private async Task LoadListsAsync()
    {
        Types = await db.DeviceTypes.OrderBy(t => t.Name).Select(t => new SelectListItem(t.Name, t.Id.ToString())).ToListAsync();
        Locations = await db.Locations.OrderBy(l => l.Name).Select(l => new SelectListItem(l.Name, l.Id.ToString())).ToListAsync();
        Customers = await db.Customers.OrderBy(c => c.Name).Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync();
        Racks = await db.Racks.OrderBy(r => r.Name).Select(r => new SelectListItem(r.Name + " (" + r.Size + " U)", r.Id.ToString())).ToListAsync();
    }
}
