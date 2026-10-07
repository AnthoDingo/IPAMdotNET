using IPAMdotNet.Data;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Maintenance;

/// <summary>
/// Reprise unique des entrées du journal écrites avant la résolution des libellés (<see cref="AppDbContext.BackfillChangeLogLabelsAsync"/>),
/// une fois l'installation faite et les migrations appliquées ; un paramètre note qu'elle est passée.
/// </summary>
public sealed class ChangeLogBackfill(IServiceScopeFactory scopes, ILogger<ChangeLogBackfill> logger) : BackgroundService
{
    /// <summary>Paramètre posé une fois la reprise faite ; à changer pour une future reprise.</summary>
    public const string DoneKey = "ChangeLog.LabelsBackfilled";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                using IServiceScope scope = scopes.CreateScope();
                if (scope.ServiceProvider.GetService<AppDbContext>() is not { } db || (await db.Database.GetPendingMigrationsAsync(stoppingToken)).Any())
                {
                    continue;
                }
                if (await db.AppSettings.AnyAsync(s => s.Key == DoneKey, stoppingToken))
                {
                    return;
                }
                int updated = await db.BackfillChangeLogLabelsAsync(stoppingToken);
                // Paramètre technique : hors journal des modifications.
                db.SuppressAudit = true;
                db.AppSettings.Add(new AppSetting { Key = DoneKey, Value = DateTime.UtcNow.ToString("O") });
                await db.SaveChangesAsync(stoppingToken);
                if (updated > 0)
                {
                    await db.LogAsync(LogSeverity.Info, LogEntry.Maintenance,
                        $"Journal des modifications : libellés ajoutés aux références de {updated} entrée(s) antérieure(s).", "Système", null);
                }
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Base injoignable ou en cours d'installation : nouvel essai à la minute suivante.
                logger.LogWarning(exception, "Reprise des libellés du journal reportée.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
