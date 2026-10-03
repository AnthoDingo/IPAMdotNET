using IPAMdotNet.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Infrastructure.Circuits.Providers;

[Authorize(Policy = "Admin")]
public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public CircuitProvider Provider { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is null)
        {
            return Page();
        }
        CircuitProvider? provider = await db.CircuitProviders.FindAsync(id);
        if (provider is null)
        {
            return NotFound();
        }
        Provider = provider;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Provider.Id = id ?? 0;
        if (await db.CircuitProviders.AnyAsync(p => p.Name == Provider.Name && p.Id != Provider.Id))
        {
            ModelState.AddModelError("Provider.Name", "Un fournisseur porte déjà ce nom.");
        }
        if (!ModelState.IsValid)
        {
            return Page();
        }
        db.Update(Provider);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        CircuitProvider? provider = await db.CircuitProviders.FindAsync(id);
        if (provider is null)
        {
            return NotFound();
        }
        if (await db.Circuits.AnyAsync(c => c.ProviderId == id))
        {
            Provider = provider;
            ModelState.AddModelError(string.Empty, "Impossible de supprimer un fournisseur qui a des circuits.");
            return Page();
        }
        db.CircuitProviders.Remove(provider);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }
}
