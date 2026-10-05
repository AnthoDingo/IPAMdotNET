using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using IPAMdotNet.Api;
using IPAMdotNet.Networking;
using Microsoft.Extensions.Options;

namespace IPAMdotNet.ScanAgent;

/// <summary>Configuration (section « Agent » d'appsettings.json ou variables d'environnement Agent__ServerUrl, Agent__Key…).</summary>
public sealed class AgentOptions
{
    public const string Section = "Agent";

    /// <summary>Adresse du serveur IPAMdotNet, ex. https://ipam.example.com.</summary>
    public string ServerUrl { get; set; } = "";

    /// <summary>Clé créée dans Administration › Agents de scan.</summary>
    public string Key { get; set; } = "";

    public int PollSeconds { get; set; } = 60;

    /// <summary>Accepter un certificat non valide (autosigné). À réserver aux tests.</summary>
    public bool IgnoreCertificateErrors { get; set; }
}

/// <summary>
/// Boucle de l'agent : demande au serveur les sous-réseaux à scanner, les sonde (ping, ports TCP),
/// résout le nom des hôtes découverts et renvoie les résultats. Le serveur décide de tout le reste (étiquettes, découvertes).
/// </summary>
public sealed class AgentWorker(IOptions<AgentOptions> options, ILogger<AgentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        AgentOptions settings = options.Value;
        if (!Uri.TryCreate(settings.ServerUrl.TrimEnd('/') + "/", UriKind.Absolute, out Uri? server) || string.IsNullOrWhiteSpace(settings.Key))
        {
            logger.LogCritical("Configuration incomplète : renseignez Agent:ServerUrl et Agent:Key (appsettings.json).");
            return;
        }
        using HttpClientHandler handler = new();
        if (settings.IgnoreCertificateErrors)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }
        using HttpClient http = new(handler) { BaseAddress = server, Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.Add(AgentProtocol.KeyHeader, settings.Key);
        http.DefaultRequestHeaders.Add(AgentProtocol.VersionHeader,
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "?");
        logger.LogInformation("Agent démarré, serveur {Server}, interrogation toutes les {Seconds} s.", server, settings.PollSeconds);

        using PeriodicTimer timer = new(TimeSpan.FromSeconds(Math.Max(10, settings.PollSeconds)));
        do
        {
            try
            {
                await CycleAsync(http, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                // Serveur injoignable, en mise à jour, clé refusée… : on réessaie au prochain passage.
                logger.LogWarning("Cycle en échec : {Message}", exception.Message);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CycleAsync(HttpClient http, CancellationToken cancellationToken)
    {
        AgentWork work = await http.GetFromJsonAsync<AgentWork>("api/agent/work", cancellationToken)
            ?? throw new InvalidOperationException("Réponse vide du serveur.");
        AgentScanSettings settings = work.Settings;
        foreach (AgentTask task in work.Subnets)
        {
            List<IPAddress> check = Parse(task.Check);
            List<IPAddress> discover = Parse(task.Discover);
            HashSet<IPAddress> online = await HostProbe.ProbeAllAsync([.. check, .. discover], TimeSpan.FromMilliseconds(settings.TimeoutMilliseconds),
                settings.Parallelism, settings.TcpPorts, cancellationToken);

            // Noms DNS et MAC vus depuis ce réseau, pour toutes les adresses en ligne (le serveur ne remplit que les champs vides).
            Dictionary<IPAddress, string> names = settings.ResolveHostnames ? await HostProbe.ResolveAllAsync(online) : [];
            Dictionary<IPAddress, string> arp = await HostProbe.ReadArpTableAsync(cancellationToken);
            Dictionary<string, string> hostnames = names.ToDictionary(p => p.Key.ToString(), p => p.Value);
            Dictionary<string, string> macs = online.Where(arp.ContainsKey).ToDictionary(a => a.ToString(), a => arp[a]);

            AgentResult result = new(task.SubnetId, [.. check.Concat(discover).Select(a => a.ToString())], [.. online.Select(a => a.ToString())], hostnames);
            using HttpResponseMessage response = await http.PostAsJsonAsync("api/agent/results", result, cancellationToken);
            response.EnsureSuccessStatusCode();
            logger.LogInformation("{Network} : {Online}/{Count} en ligne.", task.Network, online.Count, check.Count + discover.Count);
        }
    }

    private static List<IPAddress> Parse(List<string> addresses) =>
        addresses.Select(a => IPAddress.TryParse(a, out IPAddress? address) ? address : null).OfType<IPAddress>().ToList();
}
