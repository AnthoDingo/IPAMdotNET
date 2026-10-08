using System.ComponentModel.DataAnnotations;

namespace IPAMdotNet.Data;

public class Customer
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Le nom est requis."), MaxLength(100), Display(Name = "Nom")]
    public string Name { get; set; } = "";

    [MaxLength(300), Display(Name = "Adresse")]
    public string? Address { get; set; }

    [MaxLength(20), Display(Name = "Code postal")]
    public string? PostCode { get; set; }

    [MaxLength(100), Display(Name = "Ville")]
    public string? City { get; set; }

    [MaxLength(100), Display(Name = "Région / état")]
    public string? State { get; set; }

    /// <summary>Coordonnées GPS en texte invariant, comme <see cref="Location"/> (voir <see cref="Location.TryNormalizeCoordinate"/>).</summary>
    [MaxLength(20), Display(Name = "Latitude")]
    public string? Latitude { get; set; }

    [MaxLength(20), Display(Name = "Longitude")]
    public string? Longitude { get; set; }

    public bool HasCoordinates => Latitude is not null && Longitude is not null;

    [MaxLength(100), Display(Name = "Contact")]
    public string? ContactPerson { get; set; }

    [MaxLength(50), Display(Name = "Téléphone")]
    public string? ContactPhone { get; set; }

    [MaxLength(200), EmailAddress(ErrorMessage = "Adresse e-mail invalide."), Display(Name = "E-mail")]
    public string? ContactMail { get; set; }

    [MaxLength(1000), Display(Name = "Notes")]
    public string? Note { get; set; }
}
