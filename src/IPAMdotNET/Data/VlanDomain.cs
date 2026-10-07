using System.ComponentModel.DataAnnotations;

namespace IPAMdotNet.Data;

/// <summary>
/// Domaine L2 (« vlanDomains » de phpIPAM) : un numéro de VLAN est unique dans son domaine.
/// La migration crée le domaine « default », qui reçoit les VLAN sans domaine précisé.
/// </summary>
public class VlanDomain
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Le nom est requis."), MaxLength(100), Display(Name = "Nom")]
    public string Name { get; set; } = "";

    [MaxLength(500), Display(Name = "Description")]
    public string? Description { get; set; }

    /// <summary>Sections où les VLAN du domaine sont proposés aux sous-réseaux (« permissions » de phpIPAM) ; aucune = toutes.</summary>
    public List<Section> Sections { get; set; } = [];
}
