using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Network.Vlans;

[Authorize(Policy = "Admin")]
public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public Vlan Vlan { get; set; } = new();

    // Nom explicite : sans lui, si aucun champ « Custom[…] » n'est posté, ASP.NET retombe sur le préfixe vide et lit les autres champs comme clés.
    [BindProperty(Name = CustomFieldForm.Prefix)]
    public Dictionary<int, string?> Custom { get; set; } = [];

    public List<CustomFieldInput> CustomInputs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is not null)
        {
            Vlan? vlan = await db.Vlans.FindAsync(id);
            if (vlan is null)
            {
                return NotFound();
            }
            Vlan = vlan;
        }
        CustomInputs = await CustomFieldForm.LoadAsync(db, nameof(Vlan), id ?? 0);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Vlan.Id = id ?? 0;
        if (await db.Vlans.AnyAsync(v => v.Number == Vlan.Number && v.Id != Vlan.Id))
        {
            ModelState.AddModelError("Vlan.Number", "Ce numéro de VLAN existe déjà.");
        }
        List<CustomField> customFields = await CustomFieldForm.DefinitionsAsync(db, nameof(Vlan));
        Dictionary<int, string?> customValues = CustomFieldForm.Validate(customFields, Custom, ModelState);
        if (!ModelState.IsValid)
        {
            CustomInputs = CustomFieldForm.FromPosted(customFields, Custom);
            return Page();
        }
        db.Update(Vlan);
        await db.SaveChangesAsync();
        await CustomFieldForm.SaveAsync(db, Vlan.Id, customValues);
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        Vlan? vlan = await db.Vlans.FindAsync(id);
        if (vlan is null)
        {
            return NotFound();
        }
        db.Vlans.Remove(vlan);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }
}
