using IPAMdotNet.Data;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Maintenance;

/// <summary>
/// Agent de scan intégré : chaque minute, scanne les sous-réseaux dont le scan est activé et dont le dernier scan
/// est plus ancien que l'intervalle configuré. Inactif tant que l'installation n'est pas faite, que des migrations
/// sont en attente ou qu'il est désactivé (Administration › Agents de scan).
/// </summary>
public sealed class ScanAgent(IServiceScopeFactory scopes, ILogger<ScanAgent> logger) : BackgroundService
{
    // État affiché sur la page d'administration (processus courant).
    public static DateTime? LastCycleAt { get; private set; }
    public static string? LastCycleSummary { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                using IServiceScope scope = scopes.CreateScope();
                if (scope.ServiceProvider.GetService<AppDbContext>() is { } db && !(await db.Database.GetPendingMigrationsAsync(stoppingToken)).Any())
                {
                    await RunAsync(db, force: false, stoppingToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // L'agent ne doit jamais s'arrêter sur une erreur ponctuelle (base injoignable, réseau…).
                logger.LogError(exception, "Cycle de l'agent de scan en échec.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
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
            .Where(s => (s.PingCheck || s.Discover) && (force || s.LastScanAt == null || s.LastScanAt < due))
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
