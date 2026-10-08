using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IPAMdotNet.Data;

public enum NatType
{
    [Display(Name = "Source")] Source,
    [Display(Name = "Destination")] Destination,
    [Display(Name = "Statique")] Static,
}

public enum NatSideKind
{
    [Display(Name = "Source")] Source,
    [Display(Name = "Destination")] Destination,
}

public class NatRule
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Le nom est requis."), MaxLength(100), Display(Name = "Nom")]
    public string Name { get; set; } = "";

    [Display(Name = "Type")]
    public NatType Type { get; set; }

    /// <summary>Objets source et destination (plusieurs par côté, comme phpIPAM).</summary>
    public List<NatRuleObject> Objects { get; set; } = [];

    [NotMapped]
    public IEnumerable<NatRuleObject> Sources => Objects.Where(o => o.Side == NatSideKind.Source).OrderBy(o => o.Id);

    [NotMapped]
    public IEnumerable<NatRuleObject> Destinations => Objects.Where(o => o.Side == NatSideKind.Destination).OrderBy(o => o.Id);

    [Range(1, 65535, ErrorMessage = "Port entre 1 et 65535."), Display(Name = "Port source")]
    public int? SourcePort { get; set; }

    [Range(1, 65535, ErrorMessage = "Port entre 1 et 65535."), Display(Name = "Port destination")]
    public int? DestinationPort { get; set; }

    /// <summary>Équipement qui porte la règle (pare-feu, routeur).</summary>
    [Display(Name = "Équipement")]
    public int? DeviceId { get; set; }

    public Device? Device { get; set; }

    [MaxLength(500), Display(Name = "Description")]
    public string? Description { get; set; }
}

/// <summary>
/// Objet d'un côté d'une règle NAT. Le texte (adresse ou réseau, forme canonique) est recopié de l'objet lié quand il y en a un
/// et conservé s'il est supprimé ; sans objet lié, c'est une adresse externe saisie librement.
/// </summary>
public class NatRuleObject
{
    public int Id { get; set; }

    [Display(Name = "Règle NAT")]
    public int NatRuleId { get; set; }

    public NatRule? NatRule { get; set; }

    [Display(Name = "Côté")]
    public NatSideKind Side { get; set; }

    [Required, MaxLength(50), Display(Name = "Adresse ou réseau")]
    public string Text { get; set; } = "";

    [Display(Name = "Sous-réseau")]
    public int? SubnetId { get; set; }

    public Subnet? Subnet { get; set; }

    [Display(Name = "Adresse IP")]
    public int? AddressId { get; set; }

    public IpAddress? Address { get; set; }
}
