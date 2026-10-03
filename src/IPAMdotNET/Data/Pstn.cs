using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace IPAMdotNet.Data;

public enum PstnNumberState
{
    [Display(Name = "Actif")] Active,
    [Display(Name = "Réservé")] Reserved,
    [Display(Name = "Hors service")] Offline,
}

/// <summary>Préfixe téléphonique (RTC). La hiérarchie est déduite : un préfixe est rangé sous le plus long préfixe qui le commence.</summary>
public class PstnPrefix
{
    public int Id { get; set; }

    /// <summary>Chiffres, « + » initial facultatif, sans espaces (normalisé à la saisie).</summary>
    [Required(ErrorMessage = "Le préfixe est requis."), MaxLength(20), Display(Name = "Préfixe")]
    public string Prefix { get; set; } = "";

    [Required(ErrorMessage = "Le nom est requis."), MaxLength(100), Display(Name = "Nom")]
    public string Name { get; set; } = "";

    [Range(0, long.MaxValue, ErrorMessage = "Valeur positive attendue."), Display(Name = "Premier numéro")]
    public long Start { get; set; }

    [Range(0, long.MaxValue, ErrorMessage = "Valeur positive attendue."), Display(Name = "Dernier numéro")]
    public long Stop { get; set; }

    [Display(Name = "Équipement")]
    public int? DeviceId { get; set; }

    [ValidateNever]
    public Device? Device { get; set; }

    [MaxLength(500), Display(Name = "Description")]
    public string? Description { get; set; }

    [ValidateNever]
    public List<PstnNumber> Numbers { get; set; } = [];

    /// <summary>Retire espaces, points, tirets et parenthèses ; null si le résultat n'est pas « +? chiffres ».</summary>
    public static string? Normalize(string? text)
    {
        string compact = new((text ?? "").Where(c => !char.IsWhiteSpace(c) && c is not ('.' or '-' or '(' or ')')).ToArray());
        string digits = compact.StartsWith('+') ? compact[1..] : compact;
        return digits.Length > 0 && digits.All(char.IsAsciiDigit) ? compact : null;
    }
}

public class PstnNumber
{
    public int Id { get; set; }

    public int PrefixId { get; set; }

    [ValidateNever]
    public PstnPrefix? Prefix { get; set; }

    [Range(0, long.MaxValue, ErrorMessage = "Valeur positive attendue."), Display(Name = "Numéro")]
    public long Number { get; set; }

    [MaxLength(100), Display(Name = "Nom")]
    public string? Name { get; set; }

    [MaxLength(100), Display(Name = "Titulaire")]
    public string? Owner { get; set; }

    [Display(Name = "État")]
    public PstnNumberState State { get; set; }

    [Display(Name = "Équipement")]
    public int? DeviceId { get; set; }

    [ValidateNever]
    public Device? Device { get; set; }

    [MaxLength(500), Display(Name = "Description")]
    public string? Description { get; set; }
}
