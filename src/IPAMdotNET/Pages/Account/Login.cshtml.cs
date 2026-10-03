using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using IPAMdotNet.Data;
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
        // Projection sur les seules colonnes utiles : la connexion s'exécute avant l'application des migrations
        // (page /update d'AnthoDingo.Update), elle doit fonctionner même si une migration ajoute des colonnes à Users.
        User? user = await db.Users
            .Where(u => u.UserName == userName)
            .Select(u => new User { Id = u.Id, UserName = u.UserName, PasswordHash = u.PasswordHash, DisplayName = u.DisplayName, IsAdmin = u.IsAdmin })
            .SingleOrDefaultAsync();
        PasswordVerificationResult result = Hasher.VerifyHashedPassword(user!, user?.PasswordHash ?? DummyHash, Password);

        if (user is null || result == PasswordVerificationResult.Failed)
        {
            ModelState.AddModelError(string.Empty, "Nom d'utilisateur ou mot de passe incorrect.");
            return Page();
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            string newHash = Hasher.HashPassword(user, Password);
            await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.PasswordHash, newHash));
        }

        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName ?? user.UserName),
        ];
        if (user.IsAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        }

        ClaimsPrincipal principal = new(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(principal, new AuthenticationProperties { IsPersistent = RememberMe });

        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/");
    }
}

