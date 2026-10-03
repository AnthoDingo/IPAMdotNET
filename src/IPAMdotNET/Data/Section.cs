using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace IPAMdotNet.Data;

public class Section
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Le nom est requis."), MaxLength(100), Display(Name = "Nom")]
    public string Name { get; set; } = "";

    [MaxLength(500), Display(Name = "Description")]
    public string? Description { get; set; }

    /// <summary>Accès de tous les utilisateurs connectés, en plus des permissions de groupe (les admins voient tout).</summary>
    [Display(Name = "Accès par défaut")]
    public SectionAccessLevel DefaultAccess { get; set; } = SectionAccessLevel.Read;

    [ValidateNever]
    public List<Subnet> Subnets { get; set; } = [];
}
