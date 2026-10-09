using IPAMdotNet.Localization;
using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Infrastructure.Circuits;

[Authorize(Policy = "Admin")]
public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public Circuit Circuit { get; set; } = new();

    public List<SelectListItem> Providers { get; private set; } = [];
    public List<SelectListItem> Types { get; private set; } = [];
    public List<SelectListItem> Devices { get; private set; } = [];
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
            Circuit? circuit = await db.Circuits.FindAsync(id);
            if (circuit is null)
            {
                return NotFound();
            }
            Circuit = circuit;
        }
        await LoadListsAsync();
        CustomInputs = await CustomFieldForm.LoadAsync(db, nameof(Circuit), id ?? 0);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Circuit.Id = id ?? 0;
        if (await db.Circuits.AnyAsync(c => c.ProviderId == Circuit.ProviderId && c.Cid == Circuit.Cid && c.Id != Circuit.Id))
        {
            ModelState.AddModelError("Circuit.Cid", L.T("Ce fournisseur a déjà un circuit avec cet identifiant."));
        }
        List<CustomField> customFields = await CustomFieldForm.DefinitionsAsync(db, nameof(Circuit));
        Dictionary<int, string?> customValues = CustomFieldForm.Validate(customFields, Custom, ModelState);
        if (!ModelState.IsValid)
        {
            await LoadListsAsync();
            CustomInputs = CustomFieldForm.FromPosted(customFields, Custom);
            return Page();
        }
        db.Update(Circuit);
        await db.SaveChangesAsync();
        await CustomFieldForm.SaveAsync(db, Circuit.Id, customValues);
        return RedirectToPage("Index", null, $"circuit-{Circuit.Id}");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        Circuit? circuit = await db.Circuits.FindAsync(id);
        if (circuit is null)
        {
            return NotFound();
        }
        db.Circuits.Remove(circuit);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    private async Task LoadListsAsync()
    {
        Providers = await db.CircuitProviders.OrderBy(p => p.Name).Select(p => new SelectListItem(p.Name, p.Id.ToString())).ToListAsync();
        Types = await db.CircuitTypes.OrderBy(t => t.Name).Select(t => new SelectListItem(t.Name, t.Id.ToString())).ToListAsync();
        Devices = await db.Devices.OrderBy(d => d.Hostname).Select(d => new SelectListItem(d.Hostname, d.Id.ToString())).ToListAsync();
        Locations = await db.Locations.OrderBy(l => l.Name).Select(l => new SelectListItem(l.Name, l.Id.ToString())).ToListAsync();
        Customers = await db.Customers.OrderBy(c => c.Name).Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync();
    }
}
