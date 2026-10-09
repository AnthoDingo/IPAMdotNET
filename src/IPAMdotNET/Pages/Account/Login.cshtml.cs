using IPAMdotNet.Localization;
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
            ModelState.AddModelError(string.Empty, L.T("Trop d'échecs de connexion : réessayez dans {0} minute(s).", settings.LockoutMinutes));
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
        AuthMethod? directory = null;
        LdapUserInfo? directoryEntry = null;
        if (user is not null && extras.AuthMethod is not null)
        {
            directory = extras.AuthMethod;
            await LoadDirectoryOptionsAsync(directory);
            LdapResult ldap = LdapAuthenticator.Verify(directory, userName, Password, out string? error, out directoryEntry);
            if (ldap == LdapResult.ServerError)
            {
                await TryLogAsync(LogSeverity.Error, $"Annuaire injoignable : {error}", userName);
                ModelState.AddModelError(string.Empty, L.T("L'annuaire d'authentification est injoignable. Réessayez plus tard."));
                return Page();
            }
            authenticated = ldap == LdapResult.Success;
        }
        else if (user is null && await ProvisionAsync(userName) is { } provisioned)
        {
            // Compte inconnu ici mais accepté par un annuaire qui crée les comptes : créé à la volée.
            (user, directory, directoryEntry) = provisioned;
            extras = new User { UserName = "", PasswordHash = "" };
            authenticated = true;
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
            ModelState.AddModelError(string.Empty, L.T("Nom d'utilisateur ou mot de passe incorrect."));
            return Page();
        }
        if (!extras.Enabled)
        {
            await TryLogAsync(LogSeverity.Warning, "Connexion refusée : compte désactivé.", userName);
            ModelState.AddModelError(string.Empty, L.T("Ce compte est désactivé."));
            return Page();
        }
        await TryLogAsync(LogSeverity.Info, LogEntry.LoginSucceeded, user.UserName);
        if (directory is { SyncGroups: true } && directoryEntry is not null)
        {
            await SyncGroupsAsync(user.Id, directory, directoryEntry);
        }

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
    /// L'annuaire est projeté sur ses colonnes d'origine ; les options ajoutées depuis sont lues par <see cref="LoadDirectoryOptionsAsync"/>.
    /// </summary>
    private async Task<User> ExtrasAsync(int userId)
    {
        try
        {
            return await db.Users.Where(u => u.Id == userId)
                .Select(u => new User
                {
                    UserName = "",
                    PasswordHash = "",
                    Enabled = u.Enabled,
                    AuthMethod = u.AuthMethod == null ? null : new AuthMethod
                    {
                        Id = u.AuthMethod.Id,
                        Name = u.AuthMethod.Name,
                        Host = u.AuthMethod.Host,
                        Port = u.AuthMethod.Port,
                        UseSsl = u.AuthMethod.UseSsl,
                        BindTemplate = u.AuthMethod.BindTemplate,
                        TimeoutSeconds = u.AuthMethod.TimeoutSeconds,
                    },
                })
                .SingleAsync();
        }
        catch (DbException)
        {
            return new User { UserName = "", PasswordHash = "" };
        }
    }

    /// <summary>Recherche, création de compte et synchronisation des groupes : colonnes absentes avant la migration (repli : désactivées).</summary>
    private async Task LoadDirectoryOptionsAsync(AuthMethod method)
    {
        try
        {
            AuthMethod options = await db.AuthMethods.Where(a => a.Id == method.Id)
                .Select(a => new AuthMethod { SearchBase = a.SearchBase, UserFilter = a.UserFilter, SyncGroups = a.SyncGroups }).SingleAsync();
            method.SearchBase = options.SearchBase;
            method.UserFilter = options.UserFilter;
            method.SyncGroups = options.SyncGroups;
        }
        catch (DbException)
        {
        }
    }

    /// <summary>
    /// Premier annuaire « créer les comptes » qui accepte ces identifiants : le compte est créé et rattaché à cet annuaire
    /// (nom et e-mail lus dans l'annuaire). Null si aucun ne les accepte.
    /// </summary>
    private async Task<(User User, AuthMethod Method, LdapUserInfo? Entry)?> ProvisionAsync(string userName)
    {
        List<AuthMethod> methods;
        try
        {
            methods = await db.AuthMethods.AsNoTracking().Where(a => a.AutoCreateUsers).OrderBy(a => a.Id).ToListAsync();
        }
        catch (DbException)
        {
            return null;
        }
        foreach (AuthMethod method in methods)
        {
            LdapResult result = LdapAuthenticator.Verify(method, userName, Password, out string? error, out LdapUserInfo? entry);
            if (result == LdapResult.ServerError)
            {
                await TryLogAsync(LogSeverity.Error, $"Annuaire injoignable : {error}", userName);
                continue;
            }
            if (result != LdapResult.Success)
            {
                continue;
            }
            User created = new()
            {
                UserName = userName,
                PasswordHash = "",
                DisplayName = entry?.DisplayName is { Length: > 100 } longName ? longName[..100] : entry?.DisplayName,
                Email = entry?.Email is { Length: <= 200 } email ? email : null,
                AuthMethodId = method.Id,
            };
            db.AuditUserId = null;
            db.AuditUserName = $"Annuaire « {method.Name} »";
            db.Users.Add(created);
            await db.SaveChangesAsync();
            await TryLogAsync(LogSeverity.Info, $"Compte créé depuis l'annuaire « {method.Name} ».", userName);
            return (created, method, entry);
        }
        return null;
    }

    /// <summary>Groupes locaux = ceux dont le nom est celui d'un groupe de l'annuaire (comparaison sans casse).</summary>
    private async Task SyncGroupsAsync(int userId, AuthMethod method, LdapUserInfo entry)
    {
        HashSet<string> names = new(entry.Groups, StringComparer.OrdinalIgnoreCase);
        User tracked = await db.Users.Include(u => u.Groups).SingleAsync(u => u.Id == userId);
        List<Group> wanted = (await db.Groups.ToListAsync()).Where(g => names.Contains(g.Name)).ToList();
        if (tracked.Groups.Select(g => g.Id).Order().SequenceEqual(wanted.Select(g => g.Id).Order()))
        {
            return;
        }
        tracked.Groups.Clear();
        tracked.Groups.AddRange(wanted);
        await db.SaveChangesAsync();
        await TryLogAsync(LogSeverity.Info,
            $"Groupes synchronisés depuis l'annuaire « {method.Name} » : {(wanted.Count == 0 ? "aucun" : string.Join(", ", wanted.Select(g => g.Name)))}.",
            tracked.UserName);
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

