using IPAMdotNet.Localization;
using System.Net;
using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
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
    public List<Section> AllSections { get; private set; } = [];

    /// <summary>Sections où l'équipement est visible (aucune = toutes).</summary>
    [BindProperty]
    public List<int> SectionIds { get; set; } = [];

    // Nom explicite : sans lui, si aucun champ « Custom[…] » n'est posté, ASP.NET retombe sur le préfixe vide et lit les autres champs comme clés.
    [BindProperty(Name = CustomFieldForm.Prefix)]
    public Dictionary<int, string?> Custom { get; set; } = [];

    public List<CustomFieldInput> CustomInputs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is not null)
        {
            Device? device = await db.Devices.Include(d => d.Sections).SingleOrDefaultAsync(d => d.Id == id);
            if (device is null)
            {
                return NotFound();
            }
            Device = device;
            SectionIds = device.Sections.Select(s => s.Id).ToList();
        }
        await LoadListsAsync();
        CustomInputs = await CustomFieldForm.LoadAsync(db, nameof(Device), id ?? 0);
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
                ModelState.AddModelError("Device.IpAddress", L.T("Adresse IP invalide."));
            }
        }
        else
        {
            Device.IpAddress = null;
        }

        await ValidateRackPositionAsync();
        List<CustomField> customFields = await CustomFieldForm.DefinitionsAsync(db, nameof(Device));
        Dictionary<int, string?> customValues = CustomFieldForm.Validate(customFields, Custom, ModelState);

        if (!ModelState.IsValid)
        {
            await LoadListsAsync();
            CustomInputs = CustomFieldForm.FromPosted(customFields, Custom);
            return Page();
        }
        db.Update(Device);
        await db.SaveChangesAsync();
        Device tracked = await db.Devices.Include(d => d.Sections).SingleAsync(d => d.Id == Device.Id);
        List<Section> sections = await db.Sections.Where(s => SectionIds.Contains(s.Id)).ToListAsync();
        tracked.Sections.Clear();
        tracked.Sections.AddRange(sections);
        await db.SaveChangesAsync();
        await CustomFieldForm.SaveAsync(db, Device.Id, customValues);
        return RedirectToPage("Details", new { id = Device.Id });
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

    /// <summary>Position obligatoire dans un rack, contenue dans sa hauteur et sans chevauchement avec un autre équipement de la même face.</summary>
    private async Task ValidateRackPositionAsync()
    {
        if (Device.RackId is null)
        {
            Device.RackStart = null;
            Device.RackSize = null;
            Device.RackFace = RackFace.Front;
            return;
        }
        if (Device.RackStart is null || Device.RackSize is null)
        {
            ModelState.AddModelError("Device.RackStart", L.T("Position et hauteur requises pour placer l'équipement dans un rack."));
            return;
        }
        Rack? rack = await db.Racks.FindAsync(Device.RackId);
        if (rack is null)
        {
            ModelState.AddModelError("Device.RackId", L.T("Rack inconnu."));
            return;
        }
        if (Device.RackFace == RackFace.Back && !rack.HasBack)
        {
            ModelState.AddModelError("Device.RackFace", L.T("Ce rack n'a pas de face arrière."));
            return;
        }
        int start = Device.RackStart.Value;
        int end = start + Device.RackSize.Value - 1;
        if (end > rack.Size)
        {
            ModelState.AddModelError("Device.RackStart", L.T("L'équipement dépasse du rack ({0} U).", rack.Size));
            return;
        }
        Device? overlap = await db.Devices
            .Where(d => d.RackId == rack.Id && d.Id != Device.Id && d.RackFace == Device.RackFace && d.RackStart != null
                && d.RackStart <= end && start <= d.RackStart + d.RackSize - 1)
            .FirstOrDefaultAsync();
        if (overlap is not null)
        {
            ModelState.AddModelError("Device.RackStart", L.T("Ces unités sont déjà occupées par {0}.", overlap.Hostname));
        }
    }

    private async Task LoadListsAsync()
    {
        AllSections = await db.Sections.OrderBy(s => s.Name).ToListAsync();
        Types = await db.DeviceTypes.OrderBy(t => t.Name).Select(t => new SelectListItem(t.Name, t.Id.ToString())).ToListAsync();
        Locations = await db.Locations.OrderBy(l => l.Name).Select(l => new SelectListItem(l.Name, l.Id.ToString())).ToListAsync();
        Customers = await db.Customers.OrderBy(c => c.Name).Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync();
        Racks = await db.Racks.OrderBy(r => r.Name).Select(r => new SelectListItem(r.Name + " (" + r.Size + " U)", r.Id.ToString())).ToListAsync();
    }
}
