using System.ComponentModel.DataAnnotations;

namespace IPAMdotNet.Data;

public class User
{
    public int Id { get; set; }

    [Display(Name = "Nom d'utilisateur")]
    public required string UserName { get; set; }

    [Display(Name = "Mot de passe")]
    public required string PasswordHash { get; set; }

    [Display(Name = "Nom affiché")]
    public string? DisplayName { get; set; }

    [Display(Name = "Administrateur")]
    public bool IsAdmin { get; set; }

    // Colonnes ajoutées après Initial : jamais lues par la requête principale de connexion, qui s'exécute
    // avant l'application des migrations (voir Login.cshtml.cs).

    [MaxLength(200), Display(Name = "E-mail")]
    public string? Email { get; set; }

    [Display(Name = "Actif")]
    public bool Enabled { get; set; } = true;

    /// <summary>Méthode d'authentification externe (LDAP) ; null = compte local (mot de passe haché).</summary>
    [Display(Name = "Méthode d'authentification")]
    public int? AuthMethodId { get; set; }

    public AuthMethod? AuthMethod { get; set; }

    public List<Group> Groups { get; set; } = [];

    public string Label => DisplayName ?? UserName;

    // Noms d'utilisateur stockés en minuscules : comparaison identique sur les trois moteurs (Postgres est sensible à la casse).
    public static string NormalizeUserName(string userName) => userName.Trim().ToLowerInvariant();
}
