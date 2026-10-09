using IPAMdotNet.Localization;
using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IPAMdotNet.Pages.Infrastructure.Racks;

[Authorize(Policy = "Admin")]
public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public Rack Rack { get; set; } = new();

    public List<SelectListItem> Locations { get; private set; } = [];
    public List<SelectListItem> Customers { get; private set; } = [];

    // Nom explicite : sans lui, si aucun champ « Custom[…] » n'est posté, ASP.NET retombe sur le préfixe vide et lit les autres champs comme clés.
    [BindProperty(Name = CustomFieldForm.Prefix)]
    public Dictionary<int, string?> Custom { get; set; } = [];

    public List<CustomFieldInput> CustomInputs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is not null)
        {
            Rack? rack = await db.Racks.FindAsync(id);
            if (rack is null)
            {
                return NotFound();
            }
            Rack = rack;
        }
        await LoadListsAsync();
        CustomInputs = await CustomFieldForm.LoadAsync(db, nameof(Rack), id ?? 0);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Rack.Id = id ?? 0;
        int highestUnit = await db.Devices.Where(d => d.RackId == Rack.Id && d.RackStart != null)
            .Select(d => (int?)(d.RackStart + d.RackSize - 1)).MaxAsync() ?? 0;
        if (Rack.Size < highestUnit)
        {
            ModelState.AddModelError("Rack.Size", L.T("Un équipement occupe l'unité {0} : la hauteur ne peut pas être inférieure.", highestUnit));
        }
        if (!Rack.HasBack && Rack.Id != 0 && await db.Devices.AnyAsync(d => d.RackId == Rack.Id && d.RackFace == RackFace.Back))
        {
            ModelState.AddModelError("Rack.HasBack", L.T("Des équipements sont placés en face arrière : déplacez-les d'abord."));
        }
        List<CustomField> customFields = await CustomFieldForm.DefinitionsAsync(db, nameof(Rack));
        Dictionary<int, string?> customValues = CustomFieldForm.Validate(customFields, Custom, ModelState);
        if (!ModelState.IsValid)
        {
            await LoadListsAsync();
            CustomInputs = CustomFieldForm.FromPosted(customFields, Custom);
            return Page();
        }
        db.Update(Rack);
        await db.SaveChangesAsync();
        await CustomFieldForm.SaveAsync(db, Rack.Id, customValues);
        return RedirectToPage("Details", new { id = Rack.Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        Rack? rack = await db.Racks.FindAsync(id);
        if (rack is null)
        {
            return NotFound();
        }
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync();
        await db.DetachRackAsync(id);
        db.Racks.Remove(rack);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return RedirectToPage("Index");
    }

    private async Task LoadListsAsync()
    {
        Locations = await db.Locations.OrderBy(l => l.Name).Select(l => new SelectListItem(l.Name, l.Id.ToString())).ToListAsync();
        Customers = await db.Customers.OrderBy(c => c.Name).Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync();
    }
}
