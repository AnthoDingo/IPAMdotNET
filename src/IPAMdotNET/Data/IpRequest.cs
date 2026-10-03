using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace IPAMdotNet.Data;

public enum IpRequestState
{
    [Display(Name = "En attente")] Pending,
    [Display(Name = "Acceptée")] Approved,
    [Display(Name = "Refusée")] Rejected,
}

/// <summary>
/// Demande d'adresse IP dans un sous-réseau ouvert aux demandes (<see cref="Subnet.AllowRequests"/>).
/// Tant que les adresses IP ne sont pas gérées, l'adresse attribuée est conservée sur la demande.
/// </summary>
public class IpRequest
{
    public int Id { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Le sous-réseau est requis."), Display(Name = "Sous-réseau")]
    public int SubnetId { get; set; }

    [ValidateNever]
    public Subnet? Subnet { get; set; }

    [MaxLength(45), Display(Name = "Adresse souhaitée")]
    public string? RequestedAddress { get; set; }

    [MaxLength(100), Display(Name = "Nom d'hôte")]
    public string? Hostname { get; set; }

    [MaxLength(100), Display(Name = "Propriétaire")]
    public string? Owner { get; set; }

    [Required(ErrorMessage = "Le motif est requis."), MaxLength(500), Display(Name = "Motif")]
    public string Description { get; set; } = "";

    [Display(Name = "État")]
    public IpRequestState State { get; set; }

    [Display(Name = "Demandeur")]
    public int RequestedById { get; set; }

    [ValidateNever]
    public User? RequestedBy { get; set; }

    [Display(Name = "Demandée le")]
    public DateTime RequestedAt { get; set; }

    [MaxLength(45), Display(Name = "Adresse attribuée")]
    public string? AssignedAddress { get; set; }

    [MaxLength(500), Display(Name = "Commentaire")]
    public string? AdminComment { get; set; }

    [Display(Name = "Traitée par")]
    public int? ProcessedById { get; set; }

    [ValidateNever]
    public User? ProcessedBy { get; set; }

    [Display(Name = "Traitée le")]
    public DateTime? ProcessedAt { get; set; }
}
