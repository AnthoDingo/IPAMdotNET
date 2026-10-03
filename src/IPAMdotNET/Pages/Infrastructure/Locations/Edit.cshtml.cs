using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
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

    // Nom explicite : sans lui, si aucun champ « Custom[…] » n'est posté, ASP.NET retombe sur le préfixe vide et lit les autres champs comme clés.
    [BindProperty(Name = CustomFieldForm.Prefix)]
    public Dictionary<int, string?> Custom { get; set; } = [];

    public List<CustomFieldInput> CustomInputs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is not null)
        {
            Location? location = await db.Locations.FindAsync(id);
            if (location is null)
            {
                return NotFound();
            }
            Location = location;
        }
        CustomInputs = await CustomFieldForm.LoadAsync(db, nameof(Location), id ?? 0);
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
        List<CustomField> customFields = await CustomFieldForm.DefinitionsAsync(db, nameof(Location));
        Dictionary<int, string?> customValues = CustomFieldForm.Validate(customFields, Custom, ModelState);
        if (!ModelState.IsValid)
        {
            CustomInputs = CustomFieldForm.FromPosted(customFields, Custom);
            return Page();
        }
        db.Update(Location);
        await db.SaveChangesAsync();
        await CustomFieldForm.SaveAsync(db, Location.Id, customValues);
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
