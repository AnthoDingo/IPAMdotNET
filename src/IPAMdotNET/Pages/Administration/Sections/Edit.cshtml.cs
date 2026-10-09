using IPAMdotNet.Localization;
using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Administration.Sections;

public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public Section Section { get; set; } = new();

    [BindProperty(Name = CustomFieldForm.Prefix)]
    public Dictionary<int, string?> Custom { get; set; } = [];

    public List<CustomFieldInput> CustomInputs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is not null)
        {
            Section? section = await db.Sections.FindAsync(id);
            if (section is null)
            {
                return NotFound();
            }
            Section = section;
        }
        CustomInputs = await CustomFieldForm.LoadAsync(db, nameof(Section), id ?? 0);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Section.Id = id ?? 0;
        if (await db.Sections.AnyAsync(s => s.Name == Section.Name && s.Id != Section.Id))
        {
            ModelState.AddModelError("Section.Name", L.T("Une section porte déjà ce nom."));
        }
        List<CustomField> customFields = await CustomFieldForm.DefinitionsAsync(db, nameof(Section));
        Dictionary<int, string?> customValues = CustomFieldForm.Validate(customFields, Custom, ModelState);
        if (!ModelState.IsValid)
        {
            CustomInputs = CustomFieldForm.FromPosted(customFields, Custom);
            return Page();
        }
        db.Update(Section);
        await db.SaveChangesAsync();
        await CustomFieldForm.SaveAsync(db, Section.Id, customValues);
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        Section? section = await db.Sections.FindAsync(id);
        if (section is null)
        {
            return NotFound();
        }
        if (await db.Subnets.AnyAsync(s => s.SectionId == id))
        {
            Section = section;
            CustomInputs = await CustomFieldForm.LoadAsync(db, nameof(Section), id);
            ModelState.AddModelError(string.Empty, L.T("Impossible de supprimer une section qui contient des sous-réseaux."));
            return Page();
        }
        db.Sections.Remove(section);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }
}
