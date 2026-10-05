using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;

namespace IPAMdotNet.Data;

/// <summary>Clé d'accès à l'API REST. Seul le haché SHA-256 est stocké : la clé n'est affichée qu'à sa création.</summary>
public class ApiKey
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Le nom est requis."), MaxLength(100), Display(Name = "Nom")]
    public string Name { get; set; } = "";

    [MaxLength(64), Display(Name = "Empreinte")]
    public string KeyHash { get; set; } = "";

    /// <summary>Premiers caractères de la clé, pour la reconnaître dans la liste.</summary>
    [MaxLength(8), Display(Name = "Début de la clé")]
    public string KeyPrefix { get; set; } = "";

    [Display(Name = "Active")]
    public bool Enabled { get; set; } = true;

    [Display(Name = "Créée le")]
    public DateTime CreatedAt { get; set; }

    [Display(Name = "Dernière utilisation")]
    public DateTime? LastUsedAt { get; set; }

    [MaxLength(500), Display(Name = "Description")]
    public string? Description { get; set; }

    /// <summary>
    /// Compte dont la clé prend les droits (sections lisibles / modifiables) ; null = toutes les sections, comme un admin.
    /// Supprimer le compte supprime ses clés.
    /// </summary>
    [Display(Name = "Droits de l'utilisateur")]
    public int? UserId { get; set; }

    public User? User { get; set; }

    /// <summary>Création, modification et suppression autorisées (dans la limite des droits de l'utilisateur).</summary>
    [Display(Name = "Écriture")]
    public bool CanWrite { get; set; }

    public static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
}
