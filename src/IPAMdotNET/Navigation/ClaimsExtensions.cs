using System.Security.Claims;

namespace IPAMdotNet.Navigation;

public static class ClaimsExtensions
{
    /// <summary>Identifiant de l'utilisateur connecté (claim NameIdentifier posé à la connexion).</summary>
    public static int UserId(this ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Utilisateur non connecté."));
}
