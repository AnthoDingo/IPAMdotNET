using System.ComponentModel.DataAnnotations;
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

    /// <summary>Domaine par défaut : le premier créé (« default », créé par la migration).</summary>
    public static Task<int> DefaultDomainIdAsync(AppDbContext db) => db.VlanDomains.OrderBy(d => d.Id).Select(d => d.Id).FirstAsync();
}
