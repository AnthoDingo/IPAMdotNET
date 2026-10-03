using IPAMdotNet.Data;
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
    public List<SelectListItem> Locations { get; private set; } = [];
    public List<SelectListItem> Customers { get; private set; } = [];

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
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Circuit.Id = id ?? 0;
        if (await db.Circuits.AnyAsync(c => c.ProviderId == Circuit.ProviderId && c.Cid == Circuit.Cid && c.Id != Circuit.Id))
        {
            ModelState.AddModelError("Circuit.Cid", "Ce fournisseur a déjà un circuit avec cet identifiant.");
        }
        if (!ModelState.IsValid)
        {
            await LoadListsAsync();
            return Page();
        }
        db.Update(Circuit);
        await db.SaveChangesAsync();
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
        Locations = await db.Locations.OrderBy(l => l.Name).Select(l => new SelectListItem(l.Name, l.Id.ToString())).ToListAsync();
        Customers = await db.Customers.OrderBy(c => c.Name).Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync();
    }
}
