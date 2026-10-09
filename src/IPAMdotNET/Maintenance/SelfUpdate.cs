using IPAMdotNet.Localization;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using IPAMdotNet.Api;

namespace IPAMdotNet.Maintenance;

/// <summary>Version publiée sur GitHub, avec l'archive de cette plateforme (null si la release n'en a pas).</summary>
public sealed record ReleaseInfo(string Tag, string Version, bool Prerelease, DateTime? Published, string Url, ReleaseAsset? Asset);

public sealed record ReleaseAsset(string Name, string DownloadUrl, string? Digest, long Size);

/// <summary>
/// Mise à jour du serveur depuis les releases GitHub (téléchargement) ou une archive envoyée (sans accès à Internet),
/// comme l'agent distant : les fichiers en place sont renommés en .old (Windows l'autorise pour les fichiers chargés),
/// les nouveaux prennent leur place, puis l'application s'arrête avec <see cref="RestartExitCode"/> pour être relancée
/// (IIS au prochain appel, systemd Restart=on-failure, service Windows avec récupération). Les fichiers de configuration
/// (appsettings*.json, web.config) ne sont jamais remplacés ; les migrations passent ensuite par /update.
/// </summary>
public static class SelfUpdate
{
    public const string Repository = "AnthoDingo/IPAMdotNET";
    public const int RestartExitCode = 3;

    public static readonly string Version =
        AgentProtocol.CleanVersion(typeof(SelfUpdate).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion) ?? "?";

    public static string Rid => RuntimeInformation.RuntimeIdentifier;

    private static string InstallDirectory => AppContext.BaseDirectory;

    private static string AppHost => OperatingSystem.IsWindows() ? "IPAMdotNet.exe" : "IPAMdotNet";

    private static readonly SemaphoreSlim Running = new(1, 1);

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        HttpClient http = new() { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IPAMdotNet", Version));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    /// <summary>Raison pour laquelle cette installation ne peut pas se mettre à jour elle-même ; null si elle le peut.</summary>
    public static string? Unavailable(IWebHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            return L.T("Environnement de développement : la mise à jour ne s'applique qu'à une version publiée.");
        }
        if (!File.Exists(Path.Combine(InstallDirectory, AppHost)))
        {
            return L.T("Installation non issue d'une release (pas de {0} dans {1}).", AppHost, InstallDirectory);
        }
        try
        {
            string probe = Path.Combine(InstallDirectory, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return L.T("Le compte du service ne peut pas écrire dans {0}.", InstallDirectory);
        }
    }

    /// <summary>Fichiers .old laissés par la mise à jour précédente (au démarrage).</summary>
    public static void CleanUp()
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(InstallDirectory, "*.old", SearchOption.AllDirectories))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Encore verrouillé : supprimé au prochain démarrage.
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Dernières releases GitHub, de la plus récente à la plus ancienne.</summary>
    public static async Task<List<ReleaseInfo>> ReleasesAsync(CancellationToken cancellationToken)
    {
        List<GitHubRelease> releases = await Http.GetFromJsonAsync<List<GitHubRelease>>(
            $"https://api.github.com/repos/{Repository}/releases?per_page=20", cancellationToken) ?? [];
        return releases.Where(r => !r.Draft).Select(r =>
        {
            GitHubAsset? asset = r.Assets.FirstOrDefault(a =>
                a.Name == $"IPAMdotNet-{r.TagName}-{Rid}.zip" || a.Name == $"IPAMdotNet-{r.TagName}-{Rid}.tar.gz");
            return new ReleaseInfo(r.TagName, AgentProtocol.CleanVersion(r.TagName.TrimStart('v'))!, r.Prerelease, r.PublishedAt, r.HtmlUrl,
                asset is null ? null : new ReleaseAsset(asset.Name, asset.BrowserDownloadUrl, asset.Digest, asset.Size));
        }).ToList();
    }

    /// <summary>Télécharge l'archive d'une release (empreinte SHA-256 vérifiée si GitHub la fournit) et l'installe.</summary>
    public static async Task<string> InstallAsync(ReleaseAsset asset, CancellationToken cancellationToken)
    {
        string archive = Path.Combine(Path.GetTempPath(), $"ipamdotnet-{Guid.NewGuid():N}-{asset.Name}");
        try
        {
            await using (FileStream file = File.Create(archive))
            {
                await using Stream download = await Http.GetStreamAsync(asset.DownloadUrl, cancellationToken);
                await download.CopyToAsync(file, cancellationToken);
            }
            if (asset.Digest is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                await using FileStream file = File.OpenRead(archive);
                string actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellationToken));
                if (!string.Equals(actual, digest["sha256:".Length..], StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(L.T("L'empreinte SHA-256 de l'archive téléchargée ne correspond pas à celle publiée sur GitHub."));
                }
            }
            return await InstallAsync(archive, asset.Name, cancellationToken);
        }
        finally
        {
            File.Delete(archive);
        }
    }

    /// <summary>
    /// Installe une archive de release (.zip ou .tar.gz) de cette plateforme, de version supérieure ou égale à la version en cours.
    /// Renvoie la version installée. En cas d'échec pendant le remplacement, les fichiers d'origine sont remis en place.
    /// </summary>
    public static async Task<string> InstallAsync(string archive, string fileName, CancellationToken cancellationToken)
    {
        if (!await Running.WaitAsync(0, cancellationToken))
        {
            throw new InvalidOperationException(L.T("Une mise à jour est déjà en cours."));
        }
        // Dans le dossier d'installation : même volume, les déplacements sont de simples renommages.
        string staging = Path.Combine(InstallDirectory, $".update-{Guid.NewGuid():N}");
        try
        {
            Extract(archive, fileName, staging);
            string root = Directory.GetFiles(staging).Length == 0 && Directory.GetDirectories(staging) is [string single] ? single : staging;
            string dll = Path.Combine(root, "IPAMdotNet.dll");
            if (!File.Exists(dll))
            {
                throw new InvalidDataException(L.T("Ce n'est pas une archive de release d'IPAMdotNet (IPAMdotNet.dll absent)."));
            }
            if (!File.Exists(Path.Combine(root, AppHost)))
            {
                throw new InvalidDataException(L.T("Cette archive n'est pas pour cette plateforme ({0}) : {1} absent.", Rid, AppHost));
            }
            string version = AgentProtocol.CleanVersion(FileVersionInfo.GetVersionInfo(dll).ProductVersion) ?? "?";
            if (CompareVersions(version, Version) < 0)
            {
                throw new InvalidOperationException(L.T("La version {0} est antérieure à la version installée ({1}) : les migrations déjà appliquées à la base ne peuvent pas être annulées.", version, Version));
            }
            Replace(root);
            return version;
        }
        finally
        {
            try
            {
                Directory.Delete(staging, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
            Running.Release();
        }
    }

    private static void Extract(string archive, string fileName, string staging)
    {
        Directory.CreateDirectory(staging);
        // Les deux extracteurs refusent les chemins qui sortiraient du dossier cible.
        if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archive, staging);
        }
        else if (fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            using FileStream file = File.OpenRead(archive);
            using GZipStream gzip = new(file, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gzip, staging, overwriteFiles: false);
        }
        else
        {
            throw new InvalidDataException(L.T("Format attendu : archive de release .zip (Windows) ou .tar.gz (Linux)."));
        }
    }

    private static void Replace(string root)
    {
        List<string> replaced = [];
        List<string> added = [];
        try
        {
            foreach (string source in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(root, source);
                string name = Path.GetFileName(relative);
                string target = Path.Combine(InstallDirectory, relative);
                // Configuration locale : jamais remplacée si elle existe.
                if ((name.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) || name.Equals("web.config", StringComparison.OrdinalIgnoreCase))
                    && File.Exists(target))
                {
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                UnixFileMode? mode = null;
                if (File.Exists(target))
                {
                    if (!OperatingSystem.IsWindows())
                    {
                        mode = File.GetUnixFileMode(target);
                    }
                    File.Move(target, target + ".old", overwrite: true);
                    replaced.Add(target);
                }
                else
                {
                    added.Add(target);
                }
                File.Move(source, target);
                if (mode is not null && !OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(target, mode.Value);
                }
            }
        }
        catch
        {
            // Retour à l'état d'origine : une installation à moitié remplacée ne redémarrerait pas.
            foreach (string target in added.Where(File.Exists))
            {
                File.Delete(target);
            }
            foreach (string target in replaced)
            {
                File.Move(target + ".old", target, overwrite: true);
            }
            throw;
        }
    }

    /// <summary>Compare deux versions « 1.2.0 », « 1.2.0-RC3 » : une RC précède la version finale ; illisible = égale.</summary>
    public static int CompareVersions(string a, string b)
    {
        static (System.Version Number, int Candidate)? Parse(string value)
        {
            string[] parts = value.TrimStart('v').Split('-', 2);
            if (!System.Version.TryParse(parts[0], out System.Version? number))
            {
                return null;
            }
            int candidate = parts.Length == 1 ? int.MaxValue
                : parts[1].StartsWith("RC", StringComparison.OrdinalIgnoreCase) && int.TryParse(parts[1][2..], out int rc) ? rc : 0;
            return (number, candidate);
        }
        if (Parse(a) is not { } x || Parse(b) is not { } y)
        {
            return 0;
        }
        int compare = x.Number.CompareTo(y.Number);
        return compare != 0 ? compare : x.Candidate.CompareTo(y.Candidate);
    }

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("published_at")] DateTime? PublishedAt,
        [property: JsonPropertyName("assets")] List<GitHubAsset> Assets);

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl,
        [property: JsonPropertyName("digest")] string? Digest,
        [property: JsonPropertyName("size")] long Size);
}
