using System.ComponentModel.DataAnnotations;
using IPAMdotNet.Data;
using IPAMdotNet.Localization;
using IPAMdotNet.Maintenance;
using IPAMdotNet.Navigation;
using IPAMdotNet.Pages.Administration.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Profile;

/// <summary>« Mon compte » : informations personnelles et mot de passe (comptes locaux).</summary>
public class IndexModel(AppDbContext db) : PageModel
{
    [BindProperty, MaxLength(200), Display(Name = "Nom affiché")]
    public string? DisplayName { get; set; }

    [BindProperty, MaxLength(200), EmailAddress(ErrorMessage = "Adresse e-mail invalide."), Display(Name = "E-mail")]
    public string? Email { get; set; }

    [BindProperty, Display(Name = "Langue")]
    public string? Language { get; set; }

    [BindProperty, Display(Name = "Format des adresses MAC")]
    public MacFormat? MacFormat { get; set; }

    [BindProperty, DataType(DataType.Password), Display(Name = "Mot de passe actuel")]
    public string? CurrentPassword { get; set; }

    [BindProperty, DataType(DataType.Password), Display(Name = "Nouveau mot de passe")]
    public string? NewPassword { get; set; }

    [BindProperty, DataType(DataType.Password), Display(Name = "Confirmation")]
    public string? NewPasswordConfirm { get; set; }

    public User Account { get; private set; } = new() { UserName = "", PasswordHash = "" };
    public bool IsLocal => Account.AuthMethodId is null;

    [TempData]
    public string? Message { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }
        DisplayName = Account.DisplayName;
        Email = Account.Email;
        MacFormat = Account.MacFormat;
        Language = Account.Language;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadAsync())
        {
            return NotFound();
        }
        if (!string.IsNullOrEmpty(Language) && !L.IsSupported(Language))
        {
            ModelState.AddModelError(nameof(Language), L.T("Langue inconnue."));
        }
        bool changePassword = !string.IsNullOrEmpty(NewPassword);
        if (changePassword)
        {
            PasswordHasher<User> hasher = new();
            if (!IsLocal)
            {
                ModelState.AddModelError(nameof(NewPassword), L.T("Le mot de passe de ce compte est géré par l'annuaire."));
            }
            else if (string.IsNullOrEmpty(CurrentPassword)
                || Passwords.Verify(Account, Account.PasswordHash, CurrentPassword) == PasswordVerificationResult.Failed)
            {
                ModelState.AddModelError(nameof(CurrentPassword), L.T("Mot de passe actuel incorrect."));
            }
            else if (NewPassword!.Length < EditModel.MinPasswordLength)
            {
                ModelState.AddModelError(nameof(NewPassword), L.T("Le mot de passe doit faire au moins {0} caractères.", EditModel.MinPasswordLength));
            }
            else if (NewPassword != NewPasswordConfirm)
            {
                ModelState.AddModelError(nameof(NewPasswordConfirm), L.T("Les deux mots de passe ne correspondent pas."));
            }
            else
            {
                Account.PasswordHash = hasher.HashPassword(Account, NewPassword);
            }
        }
        if (!ModelState.IsValid)
        {
            return Page();
        }
        Account.DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName.Trim();
        Account.Email = string.IsNullOrWhiteSpace(Email) ? null : Email.Trim();
        Account.MacFormat = MacFormat;
        Account.Language = string.IsNullOrEmpty(Language) ? null : Language;
        await db.SaveChangesAsync();
        Message = changePassword ? L.T("Profil et mot de passe enregistrés.") : L.T("Profil enregistré.");
        return RedirectToPage();
    }

    private async Task<bool> LoadAsync()
    {
        int userId = User.UserId();
        User? account = await db.Users.Include(u => u.AuthMethod).SingleOrDefaultAsync(u => u.Id == userId);
        if (account is null)
        {
            return false;
        }
        Account = account;
        return true;
    }
}
