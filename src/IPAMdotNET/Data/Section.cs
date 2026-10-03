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

    [ValidateNever]
    public List<Subnet> Subnets { get; set; } = [];
}
