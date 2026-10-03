using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Administration.Tags;

public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public Tag Tag { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is null)
        {
            return Page();
        }
        Tag? tag = await db.Tags.FindAsync(id);
        if (tag is null)
        {
            return NotFound();
        }
        Tag = tag;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Tag.Id = id ?? 0;
        // Le caractère système (et la clé utilisée par l'agent de scan) ne se modifie pas depuis le formulaire.
        Tag? existing = id is null ? null : await db.Tags.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id);
        Tag.Locked = existing?.Locked ?? false;
        Tag.SystemKey = existing?.SystemKey;
        Tag.BackgroundColor = Tag.BackgroundColor.ToLowerInvariant();
        Tag.TextColor = Tag.TextColor.ToLowerInvariant();
        if (await db.Tags.AnyAsync(t => t.Name == Tag.Name && t.Id != Tag.Id))
        {
            ModelState.AddModelError("Tag.Name", "Une étiquette porte déjà ce nom.");
        }
        if (!ModelState.IsValid)
        {
            return Page();
        }
        db.Update(Tag);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        Tag? tag = await db.Tags.FindAsync(id);
        if (tag is null)
        {
            return NotFound();
        }
        if (tag.Locked)
        {
            Tag = tag;
            ModelState.AddModelError(string.Empty, "Une étiquette système ne peut pas être supprimée.");
            return Page();
        }
        db.Tags.Remove(tag);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }
}
