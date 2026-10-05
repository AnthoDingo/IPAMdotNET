using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Security.Claims;
using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Account;

[EnableRateLimiting("login")]
public class LoginModel(AppDbContext db) : PageModel
{
    private static readonly PasswordHasher<User> Hasher = new();

    // Hash factice : vérifié quand l'utilisateur n'existe pas, pour ne pas révéler son existence par le temps de réponse.
    private static readonly string DummyHash = Hasher.HashPassword(new User { UserName = "", PasswordHash = "" }, Guid.NewGuid().ToString());

    [BindProperty, Required(ErrorMessage = "Nom d'utilisateur requis."), Display(Name = "Nom d'utilisateur")]
    public string UserName { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "Mot de passe requis."), DataType(DataType.Password), Display(Name = "Mot de passe")]
    public string Password { get; set; } = "";

    [BindProperty, Display(Name = "Se souvenir de moi")]
    public bool RememberMe { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        string userName = Data.User.NormalizeUserName(UserName);
        ServerSettings settings = SettingsStore.Server;
        if (await IsLockedOutAsync(userName, settings))
        {
            await TryLogAsync(LogSeverity.Warning, "Connexion refusée : compte verrouillé.", userName);
            ModelState.AddModelError(string.Empty, $"Trop d'échecs de connexion : réessayez dans {settings.LockoutMinutes} minute(s).");
            return Page();
        }

        // Projection sur les seules colonnes de la migration Initial : la connexion s'exécute avant l'application
        // des migrations (page /update d'AnthoDingo.Update), elle doit fonctionner sur un schéma non migré.
        User? user = await db.Users
            .Where(u => u.UserName == userName)
            .Select(u => new User { Id = u.Id, UserName = u.UserName, PasswordHash = u.PasswordHash, DisplayName = u.DisplayName, IsAdmin = u.IsAdmin })
            .SingleOrDefaultAsync();
        User extras = user is null ? new User { UserName = "", PasswordHash = "" } : await ExtrasAsync(user.Id);

        bool authenticated;
        bool rehash = false;
        if (user is not null && extras.AuthMethod is not null)
        {
            LdapResult ldap = LdapAuthenticator.Verify(extras.AuthMethod, userName, Password, out string? error);
            if (ldap == LdapResult.ServerError)
            {
                await TryLogAsync(LogSeverity.Error, $"Annuaire injoignable : {error}", userName);
                ModelState.AddModelError(string.Empty, "L'annuaire d'authentification est injoignable. Réessayez plus tard.");
                return Page();
            }
            authenticated = ldap == LdapResult.Success;
        }
        else
        {
            // Compte local sans mot de passe (ancien compte LDAP) : refusé, avec le même coût de calcul.
            bool hasPassword = !string.IsNullOrEmpty(user?.PasswordHash);
            PasswordVerificationResult result = Passwords.Verify(user!, hasPassword ? user!.PasswordHash : DummyHash, Password);
            authenticated = user is not null && hasPassword && result != PasswordVerificationResult.Failed;
            rehash = result == PasswordVerificationResult.SuccessRehashNeeded;
        }

        if (!authenticated || user is null)
        {
            await TryLogAsync(LogSeverity.Warning, LogEntry.LoginFailed, userName);
            ModelState.AddModelError(string.Empty, "Nom d'utilisateur ou mot de passe incorrect.");
            return Page();
        }
        if (!extras.Enabled)
        {
            await TryLogAsync(LogSeverity.Warning, "Connexion refusée : compte désactivé.", userName);
            ModelState.AddModelError(string.Empty, "Ce compte est désactivé.");
            return Page();
        }
        await TryLogAsync(LogSeverity.Info, LogEntry.LoginSucceeded, user.UserName);

        if (rehash)
        {
            string newHash = Hasher.HashPassword(user, Password);
            await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.PasswordHash, newHash));
        }

        await HttpContext.SignInAsync(SessionValidator.CreatePrincipal(user), new AuthenticationProperties
        {
            IsPersistent = RememberMe,
            ExpiresUtc = DateTimeOffset.UtcNow.Add(RememberMe ? TimeSpan.FromDays(30) : TimeSpan.FromMinutes(settings.SessionMinutes)),
        });

        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/");
    }

    /// <summary>
    /// Colonnes ajoutées après Initial (actif, méthode d'authentification), lues à part : tant que la migration
    /// n'est pas appliquée elles n'existent pas, et les valeurs par défaut (actif, compte local) sont les bonnes.
    /// </summary>
    private async Task<User> ExtrasAsync(int userId)
    {
        try
        {
            return await db.Users.Where(u => u.Id == userId)
                .Select(u => new User { UserName = "", PasswordHash = "", Enabled = u.Enabled, AuthMethod = u.AuthMethod })
                .SingleAsync();
        }
        catch (DbException)
        {
            return new User { UserName = "", PasswordHash = "" };
        }
    }

    /// <summary>Verrouillage : trop d'échecs (journal système) depuis la dernière connexion réussie, dans la fenêtre de verrouillage.</summary>
    private async Task<bool> IsLockedOutAsync(string userName, ServerSettings settings)
    {
        if (settings.MaxFailedLogins == 0)
        {
            return false;
        }
        try
        {
            DateTime since = DateTime.UtcNow.AddMinutes(-settings.LockoutMinutes);
            DateTime? lastSuccess = await db.LogEntries
                .Where(l => l.Category == LogEntry.Authentication && l.UserName == userName && l.Message == LogEntry.LoginSucceeded)
                .MaxAsync(l => (DateTime?)l.Date);
            DateTime from = lastSuccess > since ? lastSuccess.Value : since;
            int failures = await db.LogEntries.CountAsync(l => l.Category == LogEntry.Authentication && l.UserName == userName
                && l.Message == LogEntry.LoginFailed && l.Date > from);
            return failures >= settings.MaxFailedLogins;
        }
        catch (DbException)
        {
            return false;
        }
    }

    private Task TryLogAsync(LogSeverity severity, string message, string userName) =>
        db.TryLogAsync(severity, LogEntry.Authentication, message, userName, HttpContext.Connection.RemoteIpAddress?.ToString());
}

