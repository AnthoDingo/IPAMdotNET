namespace IPAMdotNet.Navigation;

/// <summary>Marqueur de la vue partielle <c>_Map</c> : coordonnées en texte invariant (<see cref="Data.Location.TryNormalizeCoordinate"/>).</summary>
public sealed record MapPoint(string Label, string Latitude, string Longitude, string? Url = null);
