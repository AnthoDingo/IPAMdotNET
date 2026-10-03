using System.ComponentModel.DataAnnotations;
using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Administration.ApiKeys;

public class IndexModel(AppDbContext db) : PageModel
{
    public List<ApiKey> Keys { get; private set; } = [];

    [BindProperty, Required(ErrorMessage = "Le nom est requis."), MaxLength(100), Display(Name = "Nom")]
    public string? Name { get; set; }

    [BindProperty, MaxLength(500), Display(Name = "Description")]
    public string? Description { get; set; }

    /// <summary>Clé en clair, affichée une seule fois juste après sa création.</summary>
    [TempData]
    public string? CreatedKey { get; set; }

    public async Task OnGetAsync()
    {
        Keys = await db.ApiKeys.OrderBy(k => k.Name).ToListAsync();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (!ModelState.IsValid)
        {
            await OnGetAsync();
            return Page();
        }
        string key = ApiKey.NewKey();
        db.ApiKeys.Add(new ApiKey
        {
            Name = Name!.Trim(),
            Description = Description,
            KeyHash = ApiKey.Hash(key),
            KeyPrefix = key[..8],
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        CreatedKey = key;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        ApiKey? key = await db.ApiKeys.FindAsync(id);
        if (key is not null)
        {
            key.Enabled = !key.Enabled;
            await db.SaveChangesAsync();
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        ApiKey? key = await db.ApiKeys.FindAsync(id);
        if (key is not null)
        {
            db.ApiKeys.Remove(key);
            await db.SaveChangesAsync();
        }
        return RedirectToPage();
    }
}
