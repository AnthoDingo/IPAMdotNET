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

/// <summary>Type de circuit paramétrable (phpIPAM : circuitTypes), avec une couleur d'affichage.</summary>
public class CircuitType
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Le nom est requis."), MaxLength(50), Display(Name = "Nom")]
    public string Name { get; set; } = "";

    [Required, RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Couleur attendue au format #RRGGBB."), MaxLength(7), Display(Name = "Couleur")]
    public string Color { get; set; } = "#6c757d";

    [MaxLength(300), Display(Name = "Description")]
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

    [Display(Name = "Type")]
    public int? TypeId { get; set; }

    [ValidateNever]
    public CircuitType? Type { get; set; }

    [MaxLength(50), Display(Name = "Capacité")]
    public string? Capacity { get; set; }

    [Display(Name = "Statut")]
    public CircuitStatus Status { get; set; }

    // Chaque extrémité : un équipement et / ou un emplacement (phpIPAM : l'un ou l'autre).
    [Display(Name = "Équipement A")]
    public int? DeviceAId { get; set; }

    [ValidateNever]
    public Device? DeviceA { get; set; }

    [Display(Name = "Emplacement A")]
    public int? LocationAId { get; set; }

    [ValidateNever]
    public Location? LocationA { get; set; }

    [Display(Name = "Équipement B")]
    public int? DeviceBId { get; set; }

    [ValidateNever]
    public Device? DeviceB { get; set; }

    [Display(Name = "Emplacement B")]
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

/// <summary>Circuit logique : suite ordonnée de circuits physiques formant un lien de bout en bout (phpIPAM : circuitsLogical).</summary>
public class LogicalCircuit
{
    public int Id { get; set; }

    [Required(ErrorMessage = "L'identifiant est requis."), MaxLength(100), Display(Name = "Identifiant")]
    public string Cid { get; set; } = "";

    [MaxLength(200), Display(Name = "Usage")]
    public string? Purpose { get; set; }

    [MaxLength(500), Display(Name = "Commentaire")]
    public string? Comment { get; set; }

    [ValidateNever]
    public List<LogicalCircuitMember> Members { get; set; } = [];

    /// <summary>
    /// Remplace les membres par <paramref name="circuitIds"/>, dans cet ordre (membres chargés au préalable).
    /// Un circuit déjà membre garde sa ligne : seul son ordre change dans le journal.
    /// </summary>
    public void SetMembers(AppDbContext db, IReadOnlyList<int> circuitIds)
    {
        List<LogicalCircuitMember> removed = Members.Where(m => !circuitIds.Contains(m.CircuitId)).ToList();
        db.LogicalCircuitMembers.RemoveRange(removed);
        Members.RemoveAll(removed.Contains);
        for (int i = 0; i < circuitIds.Count; i++)
        {
            LogicalCircuitMember? member = Members.FirstOrDefault(m => m.CircuitId == circuitIds[i]);
            if (member is null)
            {
                Members.Add(new LogicalCircuitMember { CircuitId = circuitIds[i], Order = i + 1 });
            }
            else
            {
                member.Order = i + 1;
            }
        }
    }
}

public class LogicalCircuitMember
{
    public int Id { get; set; }

    [Display(Name = "Circuit logique")]
    public int LogicalCircuitId { get; set; }

    [ValidateNever]
    public LogicalCircuit? LogicalCircuit { get; set; }

    [Display(Name = "Circuit")]
    public int CircuitId { get; set; }

    [ValidateNever]
    public Circuit? Circuit { get; set; }

    [Display(Name = "Ordre")]
    public int Order { get; set; }
}
