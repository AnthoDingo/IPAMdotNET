using IPAMdotNet.Localization;
using IPAMdotNet.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IPAMdotNet.Pages.Infrastructure.DeviceTypes;

[Authorize(Policy = "Admin")]
public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public DeviceType DeviceType { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is null)
        {
            return Page();
        }
        DeviceType? type = await db.DeviceTypes.FindAsync(id);
        if (type is null)
        {
            return NotFound();
        }
        DeviceType = type;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        DeviceType.Id = id ?? 0;
        if (await db.DeviceTypes.AnyAsync(t => t.Name == DeviceType.Name && t.Id != DeviceType.Id))
        {
            ModelState.AddModelError("DeviceType.Name", L.T("Ce type existe déjà."));
        }
        if (!ModelState.IsValid)
        {
            return Page();
        }
        db.Update(DeviceType);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        DeviceType? type = await db.DeviceTypes.FindAsync(id);
        if (type is null)
        {
            return NotFound();
        }
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync();
        await db.DetachDeviceTypeAsync(id);
        db.DeviceTypes.Remove(type);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return RedirectToPage("Index");
    }
}
