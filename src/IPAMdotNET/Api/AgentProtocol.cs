namespace IPAMdotNet.Api;

// Échanges entre le serveur et l'agent de scan distant (/api/agent). Sans dépendance : ce fichier est aussi
// compilé dans IPAMdotNet.ScanAgent, les deux côtés partagent donc exactement les mêmes contrats.

/// <summary>
/// Réponse de GET /api/agent/work : réglages du scan, sous-réseaux à scanner maintenant, et version de l'agent distribuée
/// par le serveur (mise à jour automatique par GET /api/agent/update/{rid}).
/// </summary>
public sealed record AgentWork(AgentScanSettings Settings, List<AgentTask> Subnets, string? AgentVersion = null);

public sealed record AgentScanSettings(int TimeoutMilliseconds, int Parallelism, int[] TcpPorts, bool ResolveHostnames);

/// <summary>Un sous-réseau à scanner : adresses connues à vérifier, et adresses libres candidates à la découverte.</summary>
public sealed record AgentTask(int SubnetId, string Network, List<string> Check, List<string> Discover);

/// <summary>Corps de POST /api/agent/results : adresses sondées, celles qui ont répondu, leurs noms DNS et MAC éventuels (pas de MAC des anciens agents).</summary>
public sealed record AgentResult(int SubnetId, List<string> Checked, List<string> Online, Dictionary<string, string>? Hostnames,
    Dictionary<string, string>? Macs = null);

public static class AgentProtocol
{
    /// <summary>En-tête portant la clé de l'agent.</summary>
    public const string KeyHeader = "X-Agent-Key";

    /// <summary>En-tête facultatif portant la version de l'agent (affichée dans l'administration).</summary>
    public const string VersionHeader = "X-Agent-Version";

    /// <summary>Paquet de l'agent pour une plateforme (zip du dossier publié), servi par le serveur : « agent/IPAMdotNet.ScanAgent-{rid}.zip ».</summary>
    public static string PackageName(string rid) => $"IPAMdotNet.ScanAgent-{rid}.zip";

    /// <summary>Version sans métadonnées de build (« 1.2.0+abc » → « 1.2.0 »).</summary>
    public static string? CleanVersion(string? informational) => informational?.Split('+')[0];
}
