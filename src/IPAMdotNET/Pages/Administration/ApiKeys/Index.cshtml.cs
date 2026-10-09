using IPAMdotNet.Localization;
using System.ComponentModel.DataAnnotations;
using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Administration.ApiKeys;

public class IndexModel(AppDbContext db) : PageModel
{
    public List<ApiKey> Keys { get; private set; } = [];

    [BindProperty, Required(ErrorMessage = "Le nom est requis."), MaxLength(100), Display(Name = "Nom")]
    public string? Name { get; set; }

    [BindProperty, MaxLength(500), Display(Name = "Description")]
    public string? Description { get; set; }

    /// <summary>Compte dont la clé prend les droits par section ; vide = toutes les sections.</summary>
    [BindProperty, Display(Name = "Droits de l'utilisateur")]
    public int? UserId { get; set; }

    [BindProperty, Display(Name = "Autoriser l'écriture (création, modification, suppression)")]
    public bool CanWrite { get; set; }

    public List<SelectListItem> Users { get; private set; } = [];

    /// <summary>Clé en clair, affichée une seule fois juste après sa création.</summary>
    [TempData]
    public string? CreatedKey { get; set; }

    public async Task OnGetAsync()
    {
        Keys = await db.ApiKeys.Include(k => k.User).OrderBy(k => k.Name).ToListAsync();
        Users = await db.Users.OrderBy(u => u.UserName)
            .Select(u => new SelectListItem(u.DisplayName == null ? u.UserName : u.UserName + " (" + u.DisplayName + ")", u.Id.ToString())).ToListAsync();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (!ModelState.IsValid)
        {
            await OnGetAsync();
            return Page();
        }
        if (UserId is not null && !await db.Users.AnyAsync(u => u.Id == UserId))
        {
            ModelState.AddModelError(nameof(UserId), L.T("Utilisateur inconnu."));
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
            UserId = UserId,
            CanWrite = CanWrite,
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
