using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Administration.Sections;

public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public Section Section { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is null)
        {
            return Page();
        }
        Section? section = await db.Sections.FindAsync(id);
        if (section is null)
        {
            return NotFound();
        }
        Section = section;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Section.Id = id ?? 0;
        if (await db.Sections.AnyAsync(s => s.Name == Section.Name && s.Id != Section.Id))
        {
            ModelState.AddModelError("Section.Name", "Une section porte déjà ce nom.");
        }
        if (!ModelState.IsValid)
        {
            return Page();
        }
        db.Update(Section);
        await db.SaveChangesAsync();
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
            ModelState.AddModelError(string.Empty, "Impossible de supprimer une section qui contient des sous-réseaux.");
            return Page();
        }
        db.Sections.Remove(section);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }
}
