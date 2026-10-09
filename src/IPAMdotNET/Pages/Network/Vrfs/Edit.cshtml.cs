using IPAMdotNet.Localization;
using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Network.Vrfs;

[Authorize(Policy = "Admin")]
public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public Vrf Vrf { get; set; } = new();

    // Nom explicite : sans lui, si aucun champ « Custom[…] » n'est posté, ASP.NET retombe sur le préfixe vide et lit les autres champs comme clés.
    [BindProperty(Name = CustomFieldForm.Prefix)]
    public Dictionary<int, string?> Custom { get; set; } = [];

    public List<CustomFieldInput> CustomInputs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is not null)
        {
            Vrf? vrf = await db.Vrfs.FindAsync(id);
            if (vrf is null)
            {
                return NotFound();
            }
            Vrf = vrf;
        }
        CustomInputs = await CustomFieldForm.LoadAsync(db, nameof(Vrf), id ?? 0);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Vrf.Id = id ?? 0;
        if (await db.Vrfs.AnyAsync(v => v.Name == Vrf.Name && v.Id != Vrf.Id))
        {
            ModelState.AddModelError("Vrf.Name", L.T("Une VRF porte déjà ce nom."));
        }
        List<CustomField> customFields = await CustomFieldForm.DefinitionsAsync(db, nameof(Vrf));
        Dictionary<int, string?> customValues = CustomFieldForm.Validate(customFields, Custom, ModelState);
        if (!ModelState.IsValid)
        {
            CustomInputs = CustomFieldForm.FromPosted(customFields, Custom);
            return Page();
        }
        db.Update(Vrf);
        await db.SaveChangesAsync();
        await CustomFieldForm.SaveAsync(db, Vrf.Id, customValues);
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        Vrf? vrf = await db.Vrfs.FindAsync(id);
        if (vrf is null)
        {
            return NotFound();
        }
        db.Vrfs.Remove(vrf);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }
}
