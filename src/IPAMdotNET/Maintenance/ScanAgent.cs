using System.Threading.Channels;
using IPAMdotNet.Data;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Maintenance;

/// <summary>
/// Agent de scan intégré : chaque minute, scanne les sous-réseaux dont le scan est activé (hors ceux confiés à un agent distant) et dont le dernier scan
/// est plus ancien que l'intervalle configuré. Inactif tant que l'installation n'est pas faite, que des migrations
/// sont en attente ou qu'il est désactivé (Administration › Agents de scan).
/// </summary>
public sealed class ScanAgent(IServiceScopeFactory scopes, ILogger<ScanAgent> logger) : BackgroundService
{
    // État affiché sur la page d'administration (processus courant).
    public static DateTime? LastCycleAt { get; private set; }
    public static string? LastCycleSummary { get; private set; }

    // Réveils demandés par les pages (« Scanner maintenant ») : le scan tourne ici, pas dans la requête HTTP. true = cycle forcé.
    private static readonly Channel<bool> Wakeups = Channel.CreateUnbounded<bool>();

    /// <summary>Déclenche un cycle sans attendre la minute suivante (ou dès la fin du cycle en cours).</summary>
    public static void Wake(bool force) => Wakeups.Writer.TryWrite(force);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        bool force = false;
        while (true)
        {
            try
            {
                using IServiceScope scope = scopes.CreateScope();
                if (scope.ServiceProvider.GetService<AppDbContext>() is { } db && !(await db.Database.GetPendingMigrationsAsync(stoppingToken)).Any())
                {
                    await RunAsync(db, force, stoppingToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // L'agent ne doit jamais s'arrêter sur une erreur ponctuelle (base injoignable, réseau…).
                logger.LogError(exception, "Cycle de l'agent de scan en échec.");
            }
            force = await WaitAsync(stoppingToken);
        }
    }

    /// <summary>Attend une minute ou un réveil ; renvoie true si un cycle forcé a été demandé.</summary>
    private static async Task<bool> WaitAsync(CancellationToken stoppingToken)
    {
        using CancellationTokenSource delay = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        delay.CancelAfter(TimeSpan.FromMinutes(1));
        try
        {
            bool force = await Wakeups.Reader.ReadAsync(delay.Token);
            while (Wakeups.Reader.TryRead(out bool more))
            {
                force |= more;
            }
            return force;
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>Un cycle : sous-réseaux dus (ou tous ceux dont le scan est activé si <paramref name="force"/>).</summary>
    /// <returns>Nombre de sous-réseaux scannés.</returns>
    public static async Task<int> RunAsync(AppDbContext db, bool force, CancellationToken cancellationToken)
    {
        ScanSettings settings = await SettingsStore.LoadAsync<ScanSettings>(db, SettingsStore.ScanPrefix);
        if (!settings.Enabled)
        {
            return 0;
        }
        DateTime due = DateTime.UtcNow.AddMinutes(-settings.IntervalMinutes);
        List<int> subnetIds = await db.Subnets
            .Where(s => s.ScanAgentId == null && (s.PingCheck || s.Discover) && (force || s.LastScanAt == null || s.LastScanAt < due))
            .OrderBy(s => s.LastScanAt).Select(s => s.Id).ToListAsync(cancellationToken);
        int discovered = 0;
        int tagChanges = 0;
        int online = 0;
        foreach (int subnetId in subnetIds)
        {
            ScanReport report = await SubnetScanner.ScanAsync(db, subnetId, settings, cancellationToken);
            discovered += report.Discovered;
            tagChanges += report.TagChanges;
            online += report.Online;
        }
        LastCycleAt = DateTime.UtcNow;
        LastCycleSummary = $"{subnetIds.Count} sous-réseau(x) scanné(s), {online} adresse(s) en ligne, {discovered} découverte(s), {tagChanges} étiquette(s) mise(s) à jour.";
        if (discovered > 0 || tagChanges > 0)
        {
            await db.LogAsync(LogSeverity.Info, SubnetScanner.LogCategory, LastCycleSummary, SubnetScanner.AgentName, null);
        }
        return subnetIds.Count;
    }
}
