using System.ComponentModel.DataAnnotations;

namespace IPAMdotNet.Data;

public class Vlan
{
    public int Id { get; set; }

    [Range(1, 4094, ErrorMessage = "Le numéro doit être compris entre 1 et 4094."), Display(Name = "Numéro")]
    public int Number { get; set; }

    [Required(ErrorMessage = "Le nom est requis."), MaxLength(100), Display(Name = "Nom")]
    public string Name { get; set; } = "";

    [MaxLength(500), Display(Name = "Description")]
    public string? Description { get; set; }
}
