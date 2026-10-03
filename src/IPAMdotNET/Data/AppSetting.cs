using System.ComponentModel.DataAnnotations;

namespace IPAMdotNet.Data;

/// <summary>Paramètre applicatif clé / valeur (ex. texte des instructions).</summary>
public class AppSetting
{
    public const string InstructionsKey = "Instructions";

    [Key, MaxLength(100)]
    public string Key { get; set; } = "";

    [Display(Name = "Valeur")]
    public string? Value { get; set; }
}
