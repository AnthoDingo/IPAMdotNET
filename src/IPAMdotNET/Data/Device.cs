using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace IPAMdotNet.Data;

public class Device
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Le nom d'hôte est requis."), MaxLength(100), Display(Name = "Nom d'hôte")]
    public string Hostname { get; set; } = "";

    /// <summary>Adresse de management, forme canonique.</summary>
    [MaxLength(45), Display(Name = "Adresse IP")]
    public string? IpAddress { get; set; }

    [Display(Name = "Type")]
    public int? DeviceTypeId { get; set; }

    [ValidateNever]
    public DeviceType? DeviceType { get; set; }

    [MaxLength(500), Display(Name = "Description")]
    public string? Description { get; set; }

    [Display(Name = "Emplacement")]
    public int? LocationId { get; set; }

    [ValidateNever]
    public Location? Location { get; set; }

    [Display(Name = "Client")]
    public int? CustomerId { get; set; }

    [ValidateNever]
    public Customer? Customer { get; set; }

    [Display(Name = "Rack")]
    public int? RackId { get; set; }

    [ValidateNever]
    public Rack? Rack { get; set; }

    [Display(Name = "Face du rack")]
    public RackFace RackFace { get; set; }

    /// <summary>Première unité occupée (plus petit numéro) ; U1 est en bas ou en haut selon <see cref="Data.Rack.TopDown"/>.</summary>
    [Range(1, 60, ErrorMessage = "Position entre 1 et 60."), Display(Name = "Position (U)")]
    public int? RackStart { get; set; }

    [Range(1, 60, ErrorMessage = "Hauteur entre 1 et 60."), Display(Name = "Hauteur (U)")]
    public int? RackSize { get; set; }

    public int? RackEnd => RackStart + RackSize - 1;

    /// <summary>Sections où l'équipement est visible (comme phpIPAM) ; aucune = visible de tous.</summary>
    [ValidateNever]
    public List<Section> Sections { get; set; } = [];
}

public enum RackFace
{
    [Display(Name = "Avant")] Front,
    [Display(Name = "Arrière")] Back,
}
