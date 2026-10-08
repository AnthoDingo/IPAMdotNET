using System.Security.Claims;
using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;

namespace IPAMdotNet.Navigation;

public static class ClaimsExtensions
{
    /// <summary>Identifiant de l'utilisateur connecté (claim NameIdentifier posé à la connexion).</summary>
    public static int UserId(this ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Utilisateur non connecté."));

    /// <summary>Claim portant la langue choisie par l'utilisateur (absente = langue par défaut du serveur).</summary>
    public const string LanguageClaim = "ipam:language";

    /// <summary>Claim portant la préférence de format MAC (absente = format par défaut du serveur).</summary>
    public const string MacFormatClaim = "ipam:mac-format";

    /// <summary>Format MAC de l'utilisateur, ou celui d'Administration › Paramètres.</summary>
    public static MacFormat MacFormat(this ClaimsPrincipal user) =>
        Enum.TryParse(user.FindFirstValue(MacFormatClaim), out MacFormat format) ? format : SettingsStore.Server.MacFormat;

    /// <summary>MAC stockée mise au format de l'utilisateur.</summary>
    public static string? FormatMac(this ClaimsPrincipal user, string? mac) => IpAddress.FormatMac(mac, user.MacFormat());
}
