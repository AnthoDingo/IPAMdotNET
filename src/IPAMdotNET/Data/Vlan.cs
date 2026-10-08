using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Data;

public class Vlan
{
    public int Id { get; set; }

    /// <summary>Domaine L2 : le numéro est unique dans le domaine.</summary>
    [Display(Name = "Domaine L2")]
    public int DomainId { get; set; }

    public VlanDomain? Domain { get; set; }

    [Range(1, 4094, ErrorMessage = "Le numéro doit être compris entre 1 et 4094."), Display(Name = "Numéro")]
    public int Number { get; set; }

    [Required(ErrorMessage = "Le nom est requis."), MaxLength(100), Display(Name = "Nom")]
    public string Name { get; set; } = "";

    [MaxLength(500), Display(Name = "Description")]
    public string? Description { get; set; }

    [Display(Name = "Client")]
    public int? CustomerId { get; set; }

    [ValidateNever]
    public Customer? Customer { get; set; }

    /// <summary>Domaine par défaut : le premier créé (« default », créé par la migration).</summary>
    /// <summary>VLAN utilisables par un sous-réseau de la section : domaine sans restriction, ou ouvert à la section.</summary>
    public static IQueryable<Vlan> AvailableIn(IQueryable<Vlan> vlans, int sectionId) =>
        vlans.Where(v => !v.Domain!.Sections.Any() || v.Domain.Sections.Any(s => s.Id == sectionId));

    public static Task<int> DefaultDomainIdAsync(AppDbContext db) => db.VlanDomains.OrderBy(d => d.Id).Select(d => d.Id).FirstAsync();
}
