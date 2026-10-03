using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace IPAMdotNet.Data;

public class Location
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Le nom est requis."), MaxLength(100), Display(Name = "Nom")]
    public string Name { get; set; } = "";

    [MaxLength(500), Display(Name = "Description")]
    public string? Description { get; set; }

    [MaxLength(300), Display(Name = "Adresse postale")]
    public string? Address { get; set; }

    /// <summary>Coordonnées en texte invariant (point décimal), comme dans phpIPAM : évite les écarts de culture au binding.</summary>
    [MaxLength(20), Display(Name = "Latitude")]
    public string? Latitude { get; set; }

    [MaxLength(20), Display(Name = "Longitude")]
    public string? Longitude { get; set; }

    public bool HasCoordinates => Latitude is not null && Longitude is not null;

    /// <summary>Accepte « 48.85 » ou « 48,85 », vérifie |valeur| ≤ <paramref name="limit"/> et renvoie la forme invariante.</summary>
    public static bool TryNormalizeCoordinate(string? text, double limit, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }
        if (!double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            || Math.Abs(value) > limit)
        {
            return false;
        }
        normalized = value.ToString(CultureInfo.InvariantCulture);
        return true;
    }
}
