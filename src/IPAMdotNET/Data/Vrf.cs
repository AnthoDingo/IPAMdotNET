using System.ComponentModel.DataAnnotations;

namespace IPAMdotNet.Data;

public class Vrf
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Le nom est requis."), MaxLength(100), Display(Name = "Nom")]
    public string Name { get; set; } = "";

    [MaxLength(50), Display(Name = "RD (route distinguisher)")]
    public string? RouteDistinguisher { get; set; }

    [MaxLength(500), Display(Name = "Description")]
    public string? Description { get; set; }
}
