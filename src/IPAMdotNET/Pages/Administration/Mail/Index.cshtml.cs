using IPAMdotNet.Localization;
using System.ComponentModel.DataAnnotations;
using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using IPAMdotNet.Navigation;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Administration.Mail;

public class IndexModel(AppDbContext db, IDataProtectionProvider protection) : PageModel
{
    [BindProperty]
    public MailSettings Settings { get; set; } = new();

    [BindProperty, EmailAddress(ErrorMessage = "Adresse e-mail invalide."), Display(Name = "Destinataire du test")]
    public string? TestRecipient { get; set; }

    public bool HasPassword { get; private set; }

    [TempData]
    public string? Message { get; set; }

    [TempData]
    public bool MessageIsError { get; set; }

    public async Task OnGetAsync()
    {
        Settings = await SettingsStore.LoadAsync<MailSettings>(db, SettingsStore.MailPrefix);
        HasPassword = !string.IsNullOrEmpty(Settings.Password);
        Settings.Password = null;
        int userId = User.UserId();
        TestRecipient = await db.Users.Where(u => u.Id == userId).Select(u => u.Email).SingleOrDefaultAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        MailSettings current = await SettingsStore.LoadAsync<MailSettings>(db, SettingsStore.MailPrefix);
        if (Settings.Enabled && (string.IsNullOrWhiteSpace(Settings.Host) || string.IsNullOrWhiteSpace(Settings.FromAddress)))
        {
            ModelState.AddModelError(string.Empty, L.T("Le serveur et l'adresse d'expédition sont requis pour activer l'envoi."));
        }
        if (!ModelState.IsValid)
        {
            HasPassword = !string.IsNullOrEmpty(current.Password);
            return Page();
        }
        // Mot de passe vide = inchangé ; il n'est jamais renvoyé au navigateur.
        Settings.Password = string.IsNullOrEmpty(Settings.Password) ? current.Password : Mailer.Protect(protection, Settings.Password);
        await SettingsStore.SaveAsync(db, SettingsStore.MailPrefix, Settings);
        Message = L.T("Configuration enregistrée.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostClearPasswordAsync()
    {
        MailSettings current = await SettingsStore.LoadAsync<MailSettings>(db, SettingsStore.MailPrefix);
        current.Password = null;
        await SettingsStore.SaveAsync(db, SettingsStore.MailPrefix, current);
        Message = L.T("Mot de passe SMTP effacé.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestAsync()
    {
        if (string.IsNullOrWhiteSpace(TestRecipient) || !new EmailAddressAttribute().IsValid(TestRecipient))
        {
            Message = L.T("Indiquez une adresse de destination valide.");
            MessageIsError = true;
            return RedirectToPage();
        }
        string? error = await Mailer.SendAsync(db, protection, [TestRecipient], "E-mail de test",
            $"Ceci est un e-mail de test envoyé par {SettingsStore.Server.SiteTitle} à la demande de {User.Identity?.Name}.\n\nLa messagerie est correctement configurée.");
        Message = error is null ? L.T("E-mail de test envoyé à {0}.", TestRecipient) : L.T("Échec de l'envoi : {0}", error);
        MessageIsError = error is not null;
        return RedirectToPage();
    }
}
