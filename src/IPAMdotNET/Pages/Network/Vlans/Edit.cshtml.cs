using IPAMdotNet.Data;
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

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is null)
        {
            return Page();
        }
        Vlan? vlan = await db.Vlans.FindAsync(id);
        if (vlan is null)
        {
            return NotFound();
        }
        Vlan = vlan;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Vlan.Id = id ?? 0;
        if (await db.Vlans.AnyAsync(v => v.Number == Vlan.Number && v.Id != Vlan.Id))
        {
            ModelState.AddModelError("Vlan.Number", "Ce numéro de VLAN existe déjà.");
        }
        if (!ModelState.IsValid)
        {
            return Page();
        }
        db.Update(Vlan);
        await db.SaveChangesAsync();
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
