using IPAMdotNet.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Infrastructure.Pstn.Numbers;

[Authorize(Policy = "Admin")]
public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public PstnNumber Number { get; set; } = new();

    public PstnPrefix Prefix { get; private set; } = new();
    public List<SelectListItem> Devices { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id, int? prefixId)
    {
        if (id is not null)
        {
            PstnNumber? number = await db.PstnNumbers.FindAsync(id);
            if (number is null)
            {
                return NotFound();
            }
            Number = number;
        }
        else if (prefixId is not null)
        {
            Number.PrefixId = prefixId.Value;
        }
        return await LoadAsync() ? Page() : NotFound();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Number.Id = id ?? 0;
        if (!await LoadAsync())
        {
            return NotFound();
        }
        if (Number.Number < Prefix.Start || Number.Number > Prefix.Stop)
        {
            ModelState.AddModelError("Number.Number", $"Le numéro doit être compris entre {Prefix.Start} et {Prefix.Stop}.");
        }
        else if (await db.PstnNumbers.AnyAsync(n => n.PrefixId == Number.PrefixId && n.Number == Number.Number && n.Id != Number.Id))
        {
            ModelState.AddModelError("Number.Number", "Ce numéro existe déjà dans le préfixe.");
        }
        if (!ModelState.IsValid)
        {
            return Page();
        }
        db.Update(Number);
        await db.SaveChangesAsync();
        return RedirectToPage("../Details", new { id = Number.PrefixId });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        PstnNumber? number = await db.PstnNumbers.FindAsync(id);
        if (number is null)
        {
            return NotFound();
        }
        db.PstnNumbers.Remove(number);
        await db.SaveChangesAsync();
        return RedirectToPage("../Details", new { id = number.PrefixId });
    }

    private async Task<bool> LoadAsync()
    {
        PstnPrefix? prefix = await db.PstnPrefixes.FindAsync(Number.PrefixId);
        if (prefix is null)
        {
            return false;
        }
        Prefix = prefix;
        Devices = await db.Devices.OrderBy(d => d.Hostname).Select(d => new SelectListItem(d.Hostname, d.Id.ToString())).ToListAsync();
        return true;
    }
}
