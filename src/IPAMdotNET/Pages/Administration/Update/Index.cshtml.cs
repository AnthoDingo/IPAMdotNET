using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IPAMdotNet.Pages.Administration.Update;

/// <summary>Mise à jour du serveur : release GitHub choisie dans la liste, ou archive envoyée (installation sans accès à Internet).</summary>
[RequestSizeLimit(MaxArchiveSize)]
[RequestFormLimits(MultipartBodyLengthLimit = MaxArchiveSize)]
public class IndexModel(AppDbContext db, IWebHostEnvironment environment, IHostApplicationLifetime lifetime) : PageModel
{
    public const long MaxArchiveSize = 512L * 1024 * 1024;

    public string? Unavailable { get; private set; }
    public List<ReleaseInfo> Releases { get; private set; } = [];
    public string? ReleasesError { get; private set; }
    public string? Error { get; private set; }

    /// <summary>Version installée : la page annonce le redémarrage.</summary>
    public string? Installed { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Unavailable = SelfUpdate.Unavailable(environment);
        await LoadReleasesAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostInstallAsync(string tag, CancellationToken cancellationToken)
    {
        if ((Unavailable = SelfUpdate.Unavailable(environment)) is not null)
        {
            return Page();
        }
        await LoadReleasesAsync(cancellationToken);
        if (Releases.FirstOrDefault(r => r.Tag == tag)?.Asset is not { } asset)
        {
            Error = ReleasesError ?? $"Aucune archive {SelfUpdate.Rid} pour la release {tag}.";
            return Page();
        }
        return await InstallAsync(() => SelfUpdate.InstallAsync(asset, cancellationToken), $"release {tag}", cancellationToken);
    }

    public async Task<IActionResult> OnPostUploadAsync(IFormFile? archive, CancellationToken cancellationToken)
    {
        if ((Unavailable = SelfUpdate.Unavailable(environment)) is not null)
        {
            return Page();
        }
        if (archive is null || archive.Length == 0)
        {
            Error = "Choisissez l'archive de release à installer.";
            await LoadReleasesAsync(cancellationToken);
            return Page();
        }
        string path = Path.Combine(Path.GetTempPath(), $"ipamdotnet-upload-{Guid.NewGuid():N}");
        try
        {
            await using (FileStream file = System.IO.File.Create(path))
            {
                await archive.CopyToAsync(file, cancellationToken);
            }
            string name = Path.GetFileName(archive.FileName);
            return await InstallAsync(() => SelfUpdate.InstallAsync(path, name, cancellationToken), $"archive « {name} »", cancellationToken);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    private async Task<IActionResult> InstallAsync(Func<Task<string>> install, string source, CancellationToken cancellationToken)
    {
        string? ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        try
        {
            Installed = await install();
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or IOException
            or UnauthorizedAccessException or HttpRequestException)
        {
            Error = exception.Message;
            await db.LogAsync(LogSeverity.Error, LogEntry.Maintenance, $"Échec de la mise à jour ({source}) : {exception.Message}", User.Identity?.Name, ip);
            await LoadReleasesAsync(cancellationToken);
            return Page();
        }
        await db.LogAsync(LogSeverity.Warning, LogEntry.Maintenance,
            $"Mise à jour installée ({source}) : {SelfUpdate.Version} → {Installed}. Redémarrage de l'application.", User.Identity?.Name, ip);
        // Arrêt une fois la réponse envoyée ; le code de sortie fait relancer l'application par son gestionnaire de services.
        Response.OnCompleted(() =>
        {
            Environment.ExitCode = SelfUpdate.RestartExitCode;
            lifetime.StopApplication();
            return Task.CompletedTask;
        });
        return Page();
    }

    private async Task LoadReleasesAsync(CancellationToken cancellationToken)
    {
        try
        {
            Releases = await SelfUpdate.ReleasesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            ReleasesError = $"GitHub injoignable ({exception.Message}) : téléchargez l'archive depuis un autre poste et envoyez-la ci-dessous.";
        }
    }
}
