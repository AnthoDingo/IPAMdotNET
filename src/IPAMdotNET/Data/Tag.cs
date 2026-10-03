using System.ComponentModel.DataAnnotations;

namespace IPAMdotNet.Data;

/// <summary>
/// Étiquette d'état d'adresse IP (Administration › Étiquettes), comme dans phpIPAM : Hors ligne, Utilisée, Réservée, DHCP…
/// Les étiquettes système (<see cref="Locked"/>) sont créées par la migration AddTags et ne peuvent pas être supprimées.
/// </summary>
public class Tag
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Le nom est requis."), MaxLength(50), Display(Name = "Nom")]
    public string Name { get; set; } = "";

    [MaxLength(200), Display(Name = "Description")]
    public string? Description { get; set; }

    [Required, RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Couleur attendue au format #RRGGBB."), Display(Name = "Couleur de fond")]
    public string BackgroundColor { get; set; } = "#9ac0cd";

    [Required, RegularExpression("^#[0-9a-fA-F]{6}$", ErrorMessage = "Couleur attendue au format #RRGGBB."), Display(Name = "Couleur du texte")]
    public string TextColor { get; set; } = "#ffffff";

    [Display(Name = "Afficher dans la liste des adresses")]
    public bool ShowTag { get; set; } = true;

    [Display(Name = "Regrouper les plages d'adresses consécutives")]
    public bool Compress { get; set; }

    [Display(Name = "Mise à jour par le scan")]
    public bool UpdateByScan { get; set; }

    [Display(Name = "Étiquette système")]
    public bool Locked { get; set; }
}
