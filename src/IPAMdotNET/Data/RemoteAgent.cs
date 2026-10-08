using System.ComponentModel.DataAnnotations;

namespace IPAMdotNet.Data;

/// <summary>
/// Agent de scan distant (projet IPAMdotNet.ScanAgent) installé dans un autre réseau : il récupère ses sous-réseaux
/// à scanner et renvoie ses résultats par /api/agent. Authentifié par une clé dont seul le haché SHA-256 est stocké.
/// </summary>
public class RemoteAgent
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Le nom est requis."), MaxLength(100), Display(Name = "Nom")]
    public string Name { get; set; } = "";

    [MaxLength(500), Display(Name = "Description")]
    public string? Description { get; set; }

    [MaxLength(64), Display(Name = "Empreinte")]
    public string KeyHash { get; set; } = "";

    [MaxLength(8), Display(Name = "Début de la clé")]
    public string KeyPrefix { get; set; } = "";

    [Display(Name = "Actif")]
    public bool Enabled { get; set; } = true;

    [Display(Name = "Dernier contact")]
    public DateTime? LastContactAt { get; set; }

    [MaxLength(45), Display(Name = "Adresse du dernier contact")]
    public string? LastContactAddress { get; set; }

    [MaxLength(50), Display(Name = "Version")]
    public string? Version { get; set; }
}
