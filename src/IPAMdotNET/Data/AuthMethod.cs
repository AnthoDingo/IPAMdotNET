using System.ComponentModel.DataAnnotations;

namespace IPAMdotNet.Data;

/// <summary>
/// Annuaire LDAP / Active Directory. L'utilisateur est authentifié par une liaison (bind) avec le DN construit
/// depuis <see cref="BindTemplate"/> ; le compte doit exister dans IPAMdotNet (droits, groupes).
/// </summary>
public class AuthMethod
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Le nom est requis."), MaxLength(100), Display(Name = "Nom")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Le serveur est requis."), MaxLength(200), Display(Name = "Serveur")]
    public string Host { get; set; } = "";

    [Range(1, 65535, ErrorMessage = "Port entre 1 et 65535."), Display(Name = "Port")]
    public int Port { get; set; } = 389;

    [Display(Name = "LDAPS (SSL)")]
    public bool UseSsl { get; set; }

    /// <summary>« {0} » est remplacé par le nom d'utilisateur, ex. « {0}@corp.local » (AD) ou « uid={0},ou=people,dc=example,dc=com ».</summary>
    [Required(ErrorMessage = "Le modèle est requis."), MaxLength(300), Display(Name = "Modèle d'identifiant")]
    public string BindTemplate { get; set; } = "{0}";

    [Range(1, 60, ErrorMessage = "Entre 1 et 60 secondes."), Display(Name = "Délai (secondes)")]
    public int TimeoutSeconds { get; set; } = 5;

    [MaxLength(500), Display(Name = "Description")]
    public string? Description { get; set; }
}
