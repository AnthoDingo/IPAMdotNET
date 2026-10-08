using IPAMdotNet.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IPAMdotNet.Pages.Infrastructure.Circuits.Types;

[Authorize(Policy = "Admin")]
public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public CircuitType Type { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is null)
        {
            return Page();
        }
        CircuitType? type = await db.CircuitTypes.FindAsync(id);
        if (type is null)
        {
            return NotFound();
        }
        Type = type;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Type.Id = id ?? 0;
        if (await db.CircuitTypes.AnyAsync(t => t.Name == Type.Name && t.Id != Type.Id))
        {
            ModelState.AddModelError("Type.Name", "Un type porte déjà ce nom.");
        }
        if (!ModelState.IsValid)
        {
            return Page();
        }
        db.Update(Type);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    /// <summary>Les circuits de ce type perdent leur type (clé en NO ACTION, voir <see cref="AppDbContext.DetachCircuitTypeAsync"/>).</summary>
    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        CircuitType? type = await db.CircuitTypes.FindAsync(id);
        if (type is null)
        {
            return NotFound();
        }
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync();
        await db.DetachCircuitTypeAsync(id);
        db.CircuitTypes.Remove(type);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return RedirectToPage("Index");
    }
}
