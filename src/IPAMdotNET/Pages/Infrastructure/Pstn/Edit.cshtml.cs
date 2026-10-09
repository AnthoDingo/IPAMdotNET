using IPAMdotNet.Localization;
using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Infrastructure.Pstn;

[Authorize(Policy = "Admin")]
public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public PstnPrefix Prefix { get; set; } = new();

    [BindProperty(Name = CustomFieldForm.Prefix)]
    public Dictionary<int, string?> Custom { get; set; } = [];

    public List<CustomFieldInput> CustomInputs { get; private set; } = [];

    public List<SelectListItem> Devices { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is not null)
        {
            PstnPrefix? prefix = await db.PstnPrefixes.FindAsync(id);
            if (prefix is null)
            {
                return NotFound();
            }
            Prefix = prefix;
        }
        CustomInputs = await CustomFieldForm.LoadAsync(db, nameof(PstnPrefix), id ?? 0);
        await LoadDevicesAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Prefix.Id = id ?? 0;
        if (!string.IsNullOrWhiteSpace(Prefix.Prefix))
        {
            string? normalized = PstnPrefix.Normalize(Prefix.Prefix);
            if (normalized is null)
            {
                ModelState.AddModelError("Prefix.Prefix", L.T("Préfixe invalide : chiffres uniquement, « + » initial facultatif."));
            }
            else
            {
                Prefix.Prefix = normalized;
                if (await db.PstnPrefixes.AnyAsync(p => p.Prefix == normalized && p.Id != Prefix.Id))
                {
                    ModelState.AddModelError("Prefix.Prefix", L.T("Ce préfixe existe déjà."));
                }
            }
        }
        if (Prefix.Start > Prefix.Stop)
        {
            ModelState.AddModelError("Prefix.Stop", L.T("Le dernier numéro doit être supérieur ou égal au premier."));
        }
        else if (Prefix.Id != 0 && await db.PstnNumbers.AnyAsync(n => n.PrefixId == Prefix.Id && (n.Number < Prefix.Start || n.Number > Prefix.Stop)))
        {
            ModelState.AddModelError("Prefix.Stop", L.T("Des numéros existants sortiraient de la plage."));
        }
        List<CustomField> customFields = await CustomFieldForm.DefinitionsAsync(db, nameof(PstnPrefix));
        Dictionary<int, string?> customValues = CustomFieldForm.Validate(customFields, Custom, ModelState);
        if (!ModelState.IsValid)
        {
            CustomInputs = CustomFieldForm.FromPosted(customFields, Custom);
            await LoadDevicesAsync();
            return Page();
        }
        db.Update(Prefix);
        await db.SaveChangesAsync();
        await CustomFieldForm.SaveAsync(db, Prefix.Id, customValues);
        return RedirectToPage("Details", new { id = Prefix.Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        PstnPrefix? prefix = await db.PstnPrefixes.FindAsync(id);
        if (prefix is null)
        {
            return NotFound();
        }
        db.PstnPrefixes.Remove(prefix);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    private async Task LoadDevicesAsync()
    {
        Devices = await db.Devices.OrderBy(d => d.Hostname).Select(d => new SelectListItem(d.Hostname, d.Id.ToString())).ToListAsync();
    }
}
