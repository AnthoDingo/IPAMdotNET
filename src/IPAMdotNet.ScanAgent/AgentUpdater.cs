using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using IPAMdotNet.Api;

namespace IPAMdotNet.ScanAgent;

/// <summary>
/// Mise à jour automatique : quand le serveur distribue une autre version, l'agent télécharge son paquet, remplace ses
/// fichiers (l'ancien exécutable, en cours d'exécution, est renommé en .old, ce que Windows autorise) et s'arrête avec
/// un code d'erreur pour que le gestionnaire de services le relance. appsettings*.json n'est jamais remplacé.
/// </summary>
public static class AgentUpdater
{
    public static readonly string Version =
        AgentProtocol.CleanVersion(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion) ?? "?";

    /// <summary>Code de sortie après mise à jour : non nul, pour que systemd (Restart=on-failure) et Windows (récupération) relancent.</summary>
    public const int RestartExitCode = 3;

    private static string BaseDirectory => AppContext.BaseDirectory;

    // Dernière version tentée : si le paquet du serveur ne porte pas la version annoncée, on ne le réinstalle pas en boucle.
    private static string MarkerPath => Path.Combine(BaseDirectory, ".update-attempted");

    /// <summary>Fichiers .old laissés par la mise à jour précédente.</summary>
    public static void CleanUp()
    {
        foreach (string file in Directory.EnumerateFiles(BaseDirectory, "*.old", SearchOption.AllDirectories))
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

    /// <returns>true si une nouvelle version a été installée (l'agent doit redémarrer).</returns>
    public static async Task<bool> TryUpdateAsync(HttpClient http, string? serverVersion, ILogger logger, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(serverVersion) || serverVersion == Version || !IsNewerOrSame(serverVersion, Version) || AlreadyAttempted(serverVersion))
        {
            return false;
        }
        await File.WriteAllTextAsync(MarkerPath, serverVersion, cancellationToken);
        string rid = RuntimeInformation.RuntimeIdentifier;
        using HttpResponseMessage response = await http.GetAsync($"api/agent/update/{rid}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Version {Server} disponible sur le serveur, mais pas de paquet pour {Rid} ({Status}) : mise à jour manuelle.",
                serverVersion, rid, (int)response.StatusCode);
            return false;
        }
        string staging = Path.Combine(Path.GetTempPath(), $"ipamdotnet-agent-{Guid.NewGuid():N}");
        try
        {
            await using (Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken))
            {
                ZipFile.ExtractToDirectory(stream, staging);
            }
            foreach (string source in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(staging, source);
                if (Path.GetFileName(relative).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string target = Path.Combine(BaseDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                UnixFileMode? mode = null;
                if (File.Exists(target))
                {
                    if (!OperatingSystem.IsWindows())
                    {
                        mode = File.GetUnixFileMode(target);
                    }
                    File.Move(target, target + ".old", overwrite: true);
                }
                File.Move(source, target);
                if (mode is not null && !OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(target, mode.Value);
                }
            }
            logger.LogWarning("Agent mis à jour de {Old} vers {New} : redémarrage.", Version, serverVersion);
            return true;
        }
        finally
        {
            try
            {
                Directory.Delete(staging, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static bool AlreadyAttempted(string serverVersion)
    {
        try
        {
            return File.Exists(MarkerPath) && File.ReadAllText(MarkerPath).Trim() == serverVersion;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>Partie numérique (« 1.2.0-RC1 » → 1.2.0) du serveur supérieure ou égale : pas de retour en arrière.</summary>
    private static bool IsNewerOrSame(string server, string current) =>
        !System.Version.TryParse(server.Split('-')[0], out Version? s) || !System.Version.TryParse(current.Split('-')[0], out Version? c) || s >= c;
}
