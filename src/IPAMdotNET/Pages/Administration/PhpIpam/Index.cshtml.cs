using System.ComponentModel.DataAnnotations;
using System.Data.Common;
using System.Text.Json;
using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySql.Data.MySqlClient;

namespace IPAMdotNet.Pages.Administration.PhpIpam;

/// <summary>Paramètres de connexion à l'instance phpIPAM (base MySQL ou API).</summary>
public sealed class PhpIpamConnection
{
    public string Source { get; set; } = "database";

    [Display(Name = "Serveur MySQL / MariaDB")]
    public string? DbHost { get; set; }

    [Range(1, 65535), Display(Name = "Port")]
    public int DbPort { get; set; } = 3306;

    [Display(Name = "Base")]
    public string? DbName { get; set; } = "phpipam";

    [Display(Name = "Utilisateur")]
    public string? DbUser { get; set; }

    [Display(Name = "Mot de passe")]
    public string? DbPassword { get; set; }

    [Display(Name = "Adresse de phpIPAM")]
    public string? ApiUrl { get; set; }

    [Display(Name = "Application (App ID)")]
    public string? ApiApp { get; set; }

    [Display(Name = "Utilisateur")]
    public string? ApiUser { get; set; }

    [Display(Name = "Mot de passe")]
    public string? ApiPassword { get; set; }

    [Display(Name = "Code de l'application (sécurité « ssl_token » ou « none »)")]
    public string? ApiCode { get; set; }

    [Display(Name = "Accepter un certificat non valide (autosigné)")]
    public bool IgnoreCertificate { get; set; }
}

public class IndexModel(AppDbContext db, IDataProtectionProvider protection) : PageModel
{
    [BindProperty]
    public PhpIpamConnection Connection { get; set; } = new();

    /// <summary>
    /// Paramètres de connexion chiffrés après l'analyse (30 minutes) : l'import les réutilise sans renvoyer
    /// les mots de passe en clair dans la page.
    /// </summary>
    [BindProperty]
    public string? Ticket { get; set; }

    public bool TargetEmpty { get; private set; }
    public PhpIpamData? Preview { get; private set; }
    public PhpIpamImportReport? Report { get; private set; }
    public string? Error { get; private set; }

    private ITimeLimitedDataProtector Protector => protection.CreateProtector("IPAMdotNet.PhpIpamImport").ToTimeLimitedDataProtector();

    public async Task OnGetAsync()
    {
        TargetEmpty = await PhpIpamImporter.IsTargetEmptyAsync(db);
    }

    public async Task<IActionResult> OnPostAnalyzeAsync(CancellationToken cancellationToken)
    {
        TargetEmpty = await PhpIpamImporter.IsTargetEmptyAsync(db);
        Preview = await ReadAsync(cancellationToken);
        if (Preview is not null)
        {
            Ticket = Protector.Protect(JsonSerializer.Serialize(Connection), TimeSpan.FromMinutes(30));
        }
        return Page();
    }

    public async Task<IActionResult> OnPostImportAsync(CancellationToken cancellationToken)
    {
        TargetEmpty = await PhpIpamImporter.IsTargetEmptyAsync(db);
        try
        {
            Connection = JsonSerializer.Deserialize<PhpIpamConnection>(Protector.Unprotect(Ticket ?? "")) ?? new();
        }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or JsonException)
        {
            Error = "Analyse expirée : relancez l'analyse avant d'importer.";
            return Page();
        }
        if (!TargetEmpty)
        {
            Error = "L'import n'est possible que dans une installation sans données (sections, sous-réseaux, VLAN, équipements…).";
            return Page();
        }
        PhpIpamData? data = await ReadAsync(cancellationToken);
        if (data is null)
        {
            return Page();
        }
        try
        {
            Report = await new PhpIpamImporter(db, data, password => Mailer.Protect(protection, password)).ImportAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is DbException or Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            Error = $"Import annulé, rien n'a été enregistré : {exception.GetBaseException().Message}";
            return Page();
        }
        await db.LogAsync(LogSeverity.Info, LogEntry.Maintenance,
            $"Import phpIPAM ({(data.FromDatabase ? "base de données" : "API")}) : {Report}.", User.Identity?.Name, HttpContext.Connection.RemoteIpAddress?.ToString());
        Ticket = null;
        TargetEmpty = false;
        return Page();
    }

    private async Task<PhpIpamData?> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (Connection.Source == "api")
            {
                if (string.IsNullOrWhiteSpace(Connection.ApiUrl) || string.IsNullOrWhiteSpace(Connection.ApiApp)
                    || !Uri.TryCreate(Connection.ApiUrl, UriKind.Absolute, out Uri? url) || url.Scheme is not ("http" or "https"))
                {
                    Error = "Renseignez l'adresse de phpIPAM (http(s)://…) et l'identifiant de l'application d'API.";
                    return null;
                }
                return await PhpIpamSource.ReadApiAsync(Connection.ApiUrl, Connection.ApiApp, Connection.ApiUser, Connection.ApiPassword, Connection.ApiCode,
                    Connection.IgnoreCertificate, cancellationToken);
            }
            if (string.IsNullOrWhiteSpace(Connection.DbHost) || string.IsNullOrWhiteSpace(Connection.DbName) || string.IsNullOrWhiteSpace(Connection.DbUser))
            {
                Error = "Renseignez le serveur, la base et l'utilisateur MySQL.";
                return null;
            }
            // Construite par le builder (pas de concaténation) : les valeurs saisies ne peuvent pas injecter d'options.
            MySqlConnectionStringBuilder builder = new()
            {
                Server = Connection.DbHost.Trim(),
                Port = (uint)Connection.DbPort,
                Database = Connection.DbName.Trim(),
                UserID = Connection.DbUser.Trim(),
                Password = Connection.DbPassword ?? "",
                ConnectionTimeout = 15,
                DefaultCommandTimeout = 300,
                SslMode = MySqlSslMode.Preferred,
            };
            return await PhpIpamSource.ReadDatabaseAsync(builder.ConnectionString, cancellationToken);
        }
        catch (Exception exception) when (exception is DbException or HttpRequestException or InvalidOperationException or JsonException or TaskCanceledException or UriFormatException)
        {
            Error = $"Lecture de phpIPAM impossible : {exception.GetBaseException().Message}";
            return null;
        }
    }
}
