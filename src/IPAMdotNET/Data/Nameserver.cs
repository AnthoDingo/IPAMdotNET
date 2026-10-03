using System.ComponentModel.DataAnnotations;

namespace IPAMdotNet.Data;

/// <summary>Jeu de serveurs DNS, rattachable aux sous-réseaux.</summary>
public class Nameserver
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Le nom est requis."), MaxLength(100), Display(Name = "Nom")]
    public string Name { get; set; } = "";

    /// <summary>Adresses séparées par « ; », comme dans phpIPAM.</summary>
    [Required(ErrorMessage = "Au moins un serveur est requis."), MaxLength(500), Display(Name = "Serveurs")]
    public string Servers { get; set; } = "";

    [MaxLength(500), Display(Name = "Description")]
    public string? Description { get; set; }
}
