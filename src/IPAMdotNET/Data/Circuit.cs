using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace IPAMdotNet.Data;

public enum CircuitStatus
{
    [Display(Name = "Actif")] Active,
    [Display(Name = "Inactif")] Inactive,
    [Display(Name = "Réservé")] Reserved,
}

public class CircuitProvider
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Le nom est requis."), MaxLength(100), Display(Name = "Nom")]
    public string Name { get; set; } = "";

    [MaxLength(200), Display(Name = "Contact")]
    public string? Contact { get; set; }

    [MaxLength(500), Display(Name = "Description")]
    public string? Description { get; set; }
}

public class Circuit
{
    public int Id { get; set; }

    [Required(ErrorMessage = "L'identifiant du circuit est requis."), MaxLength(100), Display(Name = "Identifiant (CID)")]
    public string Cid { get; set; } = "";

    [Range(1, int.MaxValue, ErrorMessage = "Le fournisseur est requis."), Display(Name = "Fournisseur")]
    public int ProviderId { get; set; }

    [ValidateNever]
    public CircuitProvider? Provider { get; set; }

    [MaxLength(50), Display(Name = "Type")]
    public string? Type { get; set; }

    [MaxLength(50), Display(Name = "Capacité")]
    public string? Capacity { get; set; }

    [Display(Name = "Statut")]
    public CircuitStatus Status { get; set; }

    [Display(Name = "Extrémité A")]
    public int? LocationAId { get; set; }

    [ValidateNever]
    public Location? LocationA { get; set; }

    [Display(Name = "Extrémité B")]
    public int? LocationBId { get; set; }

    [ValidateNever]
    public Location? LocationB { get; set; }

    [Display(Name = "Client")]
    public int? CustomerId { get; set; }

    [ValidateNever]
    public Customer? Customer { get; set; }

    [MaxLength(500), Display(Name = "Commentaire")]
    public string? Comment { get; set; }
}
