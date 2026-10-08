using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace IPAMdotNet.Data;

public enum CustomFieldType
{
    [Display(Name = "Texte")] Text,
    [Display(Name = "Texte long")] LongText,
    [Display(Name = "Nombre")] Number,
    [Display(Name = "Oui / non")] Boolean,
    [Display(Name = "Date")] Date,
    [Display(Name = "Liste de choix")] List,
}

/// <summary>
/// Champ supplémentaire défini par un admin pour un type d'objet. phpIPAM ajoute de vraies colonnes (ALTER TABLE) :
/// non portable sur trois moteurs, les valeurs sont donc stockées dans <see cref="CustomFieldValue"/>.
/// </summary>
public class CustomField
{
    /// <summary>Types d'objets pouvant porter des champs personnalisés (clé = nom de classe).</summary>
    public static readonly IReadOnlyList<string> SupportedTypes =
        [nameof(Subnet), nameof(IpAddress), nameof(Vlan), nameof(Vrf), nameof(Device), nameof(Location), nameof(Customer), nameof(Rack), nameof(Circuit),
         nameof(Section), nameof(NatRule), nameof(BgpPeer), nameof(PstnPrefix), nameof(PstnNumber)];

    public int Id { get; set; }

    [Display(Name = "Type d'objet")]
    public string EntityType { get; set; } = "";

    [Required(ErrorMessage = "Le nom est requis."), MaxLength(100), Display(Name = "Nom")]
    public string Name { get; set; } = "";

    [Display(Name = "Type de valeur")]
    public CustomFieldType Type { get; set; }

    /// <summary>Choix possibles d'une liste, un par ligne.</summary>
    [MaxLength(2000), Display(Name = "Choix")]
    public string? Options { get; set; }

    [Display(Name = "Obligatoire")]
    public bool Required { get; set; }

    [Display(Name = "Ordre")]
    public int Order { get; set; }

    [MaxLength(300), Display(Name = "Aide")]
    public string? Description { get; set; }

    public string[] OptionList => (Options ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public class CustomFieldValue
{
    public int Id { get; set; }

    public int FieldId { get; set; }

    [ValidateNever]
    public CustomField? Field { get; set; }

    /// <summary>Identifiant de l'objet porteur (pas de clé étrangère : le type varie selon le champ).</summary>
    public int EntityId { get; set; }

    [MaxLength(2000)]
    public string Value { get; set; } = "";
}
