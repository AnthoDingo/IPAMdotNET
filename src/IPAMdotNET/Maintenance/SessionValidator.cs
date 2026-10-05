using System.Data.Common;
using System.Security.Claims;
using IPAMdotNet.Data;
using IPAMdotNet.Navigation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Maintenance;

/// <summary>
/// Revalide le cookie à chaque requête : un compte supprimé ou désactivé est déconnecté immédiatement,
/// un changement de droit administrateur, de nom affiché, de langue ou de format MAC est pris en compte sans reconnexion.
/// </summary>
public static class SessionValidator
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        AppDbContext? db = context.HttpContext.RequestServices.GetService<AppDbContext>();
        if (db is null || context.Principal is null || !int.TryParse(context.Principal.FindFirstValue(ClaimTypes.NameIdentifier), out int userId))
        {
            return;
        }
        User? user;
        try
        {
            user = await db.Users.Where(u => u.Id == userId)
                .Select(u => new User { Id = u.Id, UserName = u.UserName, PasswordHash = "", DisplayName = u.DisplayName, IsAdmin = u.IsAdmin, Enabled = u.Enabled, MacFormat = u.MacFormat, Language = u.Language })
                .SingleOrDefaultAsync();
        }
        catch (DbException)
        {
            // Schéma pas encore migré (colonnes Enabled, MacFormat… absentes) : la session reste valide jusqu'à /update.
            return;
        }
        if (user is null || !user.Enabled)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }
        if (user.IsAdmin != context.Principal.IsInRole("Admin") || user.Label != context.Principal.Identity?.Name
            || user.MacFormat?.ToString() != context.Principal.FindFirstValue(ClaimsExtensions.MacFormatClaim)
            || user.Language != context.Principal.FindFirstValue(ClaimsExtensions.LanguageClaim))
        {
            context.ReplacePrincipal(CreatePrincipal(user));
            context.ShouldRenew = true;
        }
    }

    public static ClaimsPrincipal CreatePrincipal(User user)
    {
        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Label),
        ];
        if (user.IsAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        }
        if (user.Language is { } language)
        {
            claims.Add(new Claim(ClaimsExtensions.LanguageClaim, language));
        }
        if (user.MacFormat is { } macFormat)
        {
            claims.Add(new Claim(ClaimsExtensions.MacFormatClaim, macFormat.ToString()));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }
}
