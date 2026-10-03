using System.ComponentModel.DataAnnotations;

namespace IPAMdotNet.Data;

/// <summary>Paramètres généraux du serveur (Administration › Paramètres), stockés dans <see cref="AppSetting"/>.</summary>
public sealed class ServerSettings
{
    [Required(ErrorMessage = "Le titre est requis."), MaxLength(100), Display(Name = "Titre du site")]
    public string SiteTitle { get; set; } = "IPAMdotNet";

    [MaxLength(200), Url(ErrorMessage = "URL invalide."), Display(Name = "URL du site", Description = "Utilisée dans les liens des e-mails.")]
    public string? SiteUrl { get; set; }

    [MaxLength(100), Display(Name = "Nom de l'administrateur")]
    public string? AdminName { get; set; }

    [MaxLength(200), EmailAddress(ErrorMessage = "Adresse e-mail invalide."), Display(Name = "E-mail de l'administrateur", Description = "Affiché en pied de page comme contact.")]
    public string? AdminEmail { get; set; }

    [Display(Name = "Demandes d'adresses")]
    public bool EnableIpRequests { get; set; } = true;

    [Display(Name = "Journal des modifications")]
    public bool EnableChangelog { get; set; } = true;

    [Range(0, 100, ErrorMessage = "Entre 0 et 100."), Display(Name = "Échecs de connexion avant verrouillage", Description = "0 = pas de verrouillage.")]
    public int MaxFailedLogins { get; set; } = 5;

    [Range(1, 1440, ErrorMessage = "Entre 1 et 1440 minutes."), Display(Name = "Durée du verrouillage (minutes)")]
    public int LockoutMinutes { get; set; } = 15;

    [Range(5, 43200, ErrorMessage = "Entre 5 et 43200 minutes."), Display(Name = "Durée de session (minutes)", Description = "Hors « Se souvenir de moi » (30 jours).")]
    public int SessionMinutes { get; set; } = 480;
}

/// <summary>Configuration SMTP (Administration › Messagerie).</summary>
public sealed class MailSettings
{
    [Display(Name = "Envoi d'e-mails activé")]
    public bool Enabled { get; set; }

    [MaxLength(200), Display(Name = "Serveur SMTP")]
    public string? Host { get; set; }

    [Range(1, 65535, ErrorMessage = "Port entre 1 et 65535."), Display(Name = "Port")]
    public int Port { get; set; } = 587;

    [Display(Name = "Chiffrement TLS (STARTTLS)")]
    public bool UseTls { get; set; } = true;

    [MaxLength(200), Display(Name = "Utilisateur")]
    public string? UserName { get; set; }

    /// <summary>Chiffré par la protection des données d'ASP.NET Core avant stockage.</summary>
    [MaxLength(2000), Display(Name = "Mot de passe")]
    public string? Password { get; set; }

    [MaxLength(200), EmailAddress(ErrorMessage = "Adresse e-mail invalide."), Display(Name = "Adresse d'expédition")]
    public string? FromAddress { get; set; }

    [MaxLength(100), Display(Name = "Nom d'expédition")]
    public string? FromName { get; set; }

    public bool IsUsable => Enabled && !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);
}

/// <summary>Widgets du tableau de bord affichés (Administration › Widgets).</summary>
public sealed class WidgetSettings
{
    public static readonly IReadOnlyList<(string Key, string Label)> All =
    [
        ("statistics", "Statistiques"),
        ("favorites", "Sous-réseaux favoris"),
        ("requests", "Demandes d'adresses"),
        ("top-ipv4", "Top 10 sous-réseaux IPv4"),
        ("top-ipv6", "Top 10 sous-réseaux IPv6"),
        ("changes", "Dernières modifications"),
    ];

    /// <summary>Clés des widgets masqués (par défaut : aucun).</summary>
    public string Hidden { get; set; } = "";

    public bool IsVisible(string key) => !Hidden.Split(',').Contains(key);
}
