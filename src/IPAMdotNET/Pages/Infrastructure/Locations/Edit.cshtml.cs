using IPAMdotNet.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore.Storage;

namespace IPAMdotNet.Pages.Infrastructure.Locations;

[Authorize(Policy = "Admin")]
public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public Location Location { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is null)
        {
            return Page();
        }
        Location? location = await db.Locations.FindAsync(id);
        if (location is null)
        {
            return NotFound();
        }
        Location = location;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Location.Id = id ?? 0;
        if (Location.TryNormalizeCoordinate(Location.Latitude, 90, out string? latitude))
        {
            Location.Latitude = latitude;
        }
        else
        {
            ModelState.AddModelError("Location.Latitude", "Latitude invalide (nombre entre -90 et 90).");
        }
        if (Location.TryNormalizeCoordinate(Location.Longitude, 180, out string? longitude))
        {
            Location.Longitude = longitude;
        }
        else
        {
            ModelState.AddModelError("Location.Longitude", "Longitude invalide (nombre entre -180 et 180).");
        }
        if (!ModelState.IsValid)
        {
            return Page();
        }
        db.Update(Location);
        await db.SaveChangesAsync();
        return RedirectToPage("Details", new { id = Location.Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        Location? location = await db.Locations.FindAsync(id);
        if (location is null)
        {
            return NotFound();
        }
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync();
        await db.DetachLocationAsync(id);
        db.Locations.Remove(location);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return RedirectToPage("Index");
    }
}
