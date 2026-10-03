using System.ComponentModel.DataAnnotations;

namespace IPAMdotNet.Data;

public enum NatType
{
    [Display(Name = "Source")] Source,
    [Display(Name = "Destination")] Destination,
    [Display(Name = "Statique")] Static,
}

public class NatRule
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Le nom est requis."), MaxLength(100), Display(Name = "Nom")]
    public string Name { get; set; } = "";

    [Display(Name = "Type")]
    public NatType Type { get; set; }

    /// <summary>Adresse ou réseau, forme canonique.</summary>
    [Required(ErrorMessage = "La source est requise."), MaxLength(50), Display(Name = "Source")]
    public string Source { get; set; } = "";

    [Range(1, 65535, ErrorMessage = "Port entre 1 et 65535."), Display(Name = "Port source")]
    public int? SourcePort { get; set; }

    [Required(ErrorMessage = "La destination est requise."), MaxLength(50), Display(Name = "Destination")]
    public string Destination { get; set; } = "";

    [Range(1, 65535, ErrorMessage = "Port entre 1 et 65535."), Display(Name = "Port destination")]
    public int? DestinationPort { get; set; }

    [MaxLength(500), Display(Name = "Description")]
    public string? Description { get; set; }
}
