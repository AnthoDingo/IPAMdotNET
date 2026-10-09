using IPAMdotNet.Localization;
using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Administration.CustomFields;

public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public CustomField Field { get; set; } = new();

    public List<SelectListItem> EntityTypes { get; } = CustomField.SupportedTypes
        .Select(t => new SelectListItem(L.T(ChangeLog.Types[t].Label), t))
        .ToList();

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is null)
        {
            return Page();
        }
        CustomField? field = await db.CustomFields.FindAsync(id);
        if (field is null)
        {
            return NotFound();
        }
        Field = field;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Field.Id = id ?? 0;
        if (!CustomField.SupportedTypes.Contains(Field.EntityType))
        {
            ModelState.AddModelError("Field.EntityType", L.T("Type d'objet inconnu."));
        }
        if (await db.CustomFields.AnyAsync(f => f.EntityType == Field.EntityType && f.Name == Field.Name && f.Id != Field.Id))
        {
            ModelState.AddModelError("Field.Name", L.T("Ce type d'objet a déjà un champ de ce nom."));
        }
        if (Field.Type == CustomFieldType.List)
        {
            Field.Options = string.Join('\n', Field.OptionList.Distinct());
            if (Field.OptionList.Length == 0)
            {
                ModelState.AddModelError("Field.Options", L.T("Une liste doit proposer au moins un choix."));
            }
        }
        else
        {
            Field.Options = null;
        }
        if (Field.Id != 0 && await db.CustomFields.AnyAsync(f => f.Id == Field.Id && f.EntityType != Field.EntityType))
        {
            ModelState.AddModelError("Field.EntityType", L.T("Le type d'objet d'un champ existant ne peut pas changer."));
        }
        if (!ModelState.IsValid)
        {
            return Page();
        }
        db.Update(Field);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        CustomField? field = await db.CustomFields.FindAsync(id);
        if (field is null)
        {
            return NotFound();
        }
        db.CustomFields.Remove(field);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }
}
