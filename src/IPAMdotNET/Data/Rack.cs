using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace IPAMdotNet.Data;

public class Rack
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Le nom est requis."), MaxLength(100), Display(Name = "Nom")]
    public string Name { get; set; } = "";

    [Range(1, 60, ErrorMessage = "Hauteur entre 1 et 60 U."), Display(Name = "Hauteur (U)")]
    public int Size { get; set; } = 42;

    [Display(Name = "Emplacement")]
    public int? LocationId { get; set; }

    [ValidateNever]
    public Location? Location { get; set; }

    [Display(Name = "Client")]
    public int? CustomerId { get; set; }

    [ValidateNever]
    public Customer? Customer { get; set; }

    [MaxLength(500), Display(Name = "Description")]
    public string? Description { get; set; }

    [ValidateNever]
    public List<Device> Devices { get; set; } = [];
}
