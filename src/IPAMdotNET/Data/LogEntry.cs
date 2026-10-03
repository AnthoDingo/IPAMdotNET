using System.ComponentModel.DataAnnotations;

namespace IPAMdotNet.Data;

public enum LogSeverity
{
    [Display(Name = "Information")] Info,
    [Display(Name = "Avertissement")] Warning,
    [Display(Name = "Erreur")] Error,
}

/// <summary>Journal système (connexions, opérations de maintenance), distinct du journal des modifications d'objets.</summary>
public class LogEntry
{
    public const string Authentication = "Connexion";
    public const string Maintenance = "Maintenance";

    public int Id { get; set; }

    public DateTime Date { get; set; }

    public LogSeverity Severity { get; set; }

    [MaxLength(50)]
    public string Category { get; set; } = "";

    [MaxLength(1000)]
    public string Message { get; set; } = "";

    [MaxLength(200)]
    public string? UserName { get; set; }

    [MaxLength(45)]
    public string? IpAddress { get; set; }
}
