using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace IPAMdotNet.Data;

public enum SectionAccessLevel
{
    [Display(Name = "Aucun accès")] None,
    [Display(Name = "Lecture")] Read,
    [Display(Name = "Lecture et écriture")] Write,
}

/// <summary>Groupe d'utilisateurs, porteur de permissions par section (comme phpIPAM).</summary>
public class Group
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Le nom est requis."), MaxLength(100), Display(Name = "Nom")]
    public string Name { get; set; } = "";

    [MaxLength(500), Display(Name = "Description")]
    public string? Description { get; set; }

    [ValidateNever]
    public List<User> Users { get; set; } = [];
}

public class SectionPermission
{
    public int SectionId { get; set; }
    public Section? Section { get; set; }
    public int GroupId { get; set; }
    public Group? Group { get; set; }
    public SectionAccessLevel Level { get; set; }
}
