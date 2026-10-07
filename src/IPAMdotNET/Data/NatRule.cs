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

    /// <summary>
    /// Adresse ou réseau, forme canonique. Recopié de l'objet lié quand il y en a un (et conservé s'il est supprimé),
    /// saisi librement sinon (adresse externe).
    /// </summary>
    [Required(ErrorMessage = "La source est requise."), MaxLength(50), Display(Name = "Source")]
    public string Source { get; set; } = "";

    /// <summary>Sous-réseau lié à la source (au plus un objet par côté : sous-réseau ou adresse).</summary>
    [Display(Name = "Sous-réseau source")]
    public int? SourceSubnetId { get; set; }

    public Subnet? SourceSubnet { get; set; }

    [Display(Name = "Adresse source")]
    public int? SourceAddressId { get; set; }

    public IpAddress? SourceAddress { get; set; }

    [Range(1, 65535, ErrorMessage = "Port entre 1 et 65535."), Display(Name = "Port source")]
    public int? SourcePort { get; set; }

    [Required(ErrorMessage = "La destination est requise."), MaxLength(50), Display(Name = "Destination")]
    public string Destination { get; set; } = "";

    [Display(Name = "Sous-réseau destination")]
    public int? DestinationSubnetId { get; set; }

    public Subnet? DestinationSubnet { get; set; }

    [Display(Name = "Adresse destination")]
    public int? DestinationAddressId { get; set; }

    public IpAddress? DestinationAddress { get; set; }

    [Range(1, 65535, ErrorMessage = "Port entre 1 et 65535."), Display(Name = "Port destination")]
    public int? DestinationPort { get; set; }

    /// <summary>Équipement qui porte la règle (pare-feu, routeur).</summary>
    [Display(Name = "Équipement")]
    public int? DeviceId { get; set; }

    public Device? Device { get; set; }

    [MaxLength(500), Display(Name = "Description")]
    public string? Description { get; set; }
}
