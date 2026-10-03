using IPAMdotNet.Data;
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

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is null)
        {
            return Page();
        }
        Vrf? vrf = await db.Vrfs.FindAsync(id);
        if (vrf is null)
        {
            return NotFound();
        }
        Vrf = vrf;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Vrf.Id = id ?? 0;
        if (await db.Vrfs.AnyAsync(v => v.Name == Vrf.Name && v.Id != Vrf.Id))
        {
            ModelState.AddModelError("Vrf.Name", "Une VRF porte déjà ce nom.");
        }
        if (!ModelState.IsValid)
        {
            return Page();
        }
        db.Update(Vrf);
        await db.SaveChangesAsync();
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
