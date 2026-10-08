using IPAMdotNet.Data;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Maintenance;

/// <summary>Erreurs applicatives écrites dans le journal système (Administration › Journaux).</summary>
public static class ErrorLog
{
    /// <summary>
    /// Écrit l'exception dans un contexte neuf (celui de la requête peut être dans un état invalide). Ne lève jamais :
    /// base injoignable ou non migrée, l'erreur reste dans les journaux ASP.NET Core.
    /// </summary>
    public static async Task WriteAsync(IServiceScopeFactory scopes, Exception exception, string source, string? userName = null, string? ipAddress = null)
    {
        try
        {
            using IServiceScope scope = scopes.CreateScope();
            if (scope.ServiceProvider.GetService<AppDbContext>() is { } db)
            {
                await db.LogAsync(LogSeverity.Error, LogEntry.Application, $"{source} — {exception}", userName, ipAddress);
            }
        }
        catch (Exception)
        {
            // Rien de plus à faire : l'appelant journalise ou relance l'exception d'origine.
        }
    }

    /// <summary>Journalise toute exception non gérée d'une requête, puis la relance (page d'erreur inchangée).</summary>
    public static IApplicationBuilder UseErrorLogging(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        try
        {
            await next();
        }
        catch (Exception exception) when (!context.RequestAborted.IsCancellationRequested)
        {
            await WriteAsync(context.RequestServices.GetRequiredService<IServiceScopeFactory>(), exception,
                $"{context.Request.Method} {context.Request.Path}", context.User.Identity?.Name, context.Connection.RemoteIpAddress?.ToString());
            throw;
        }
    });
}

/// <summary>
/// Purge du journal système : supprime chaque heure les entrées plus anciennes que <see cref="ServerSettings.LogRetentionDays"/>
/// (0 = conservation illimitée). Limite au jour près, pour qu'une seule purge par jour supprime quelque chose.
/// </summary>
public sealed class LogPurge(IServiceScopeFactory scopes, ILogger<LogPurge> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromHours(1));
        do
        {
            try
            {
                using IServiceScope scope = scopes.CreateScope();
                if (scope.ServiceProvider.GetService<AppDbContext>() is not { } db || (await db.Database.GetPendingMigrationsAsync(stoppingToken)).Any())
                {
                    continue;
                }
                ServerSettings settings = await SettingsStore.LoadAsync<ServerSettings>(db, SettingsStore.ServerPrefix);
                if (settings.LogRetentionDays <= 0)
                {
                    continue;
                }
                DateTime cutoff = DateTime.UtcNow.Date.AddDays(-settings.LogRetentionDays);
                int deleted = await db.LogEntries.Where(l => l.Date < cutoff).ExecuteDeleteAsync(stoppingToken);
                if (deleted > 0)
                {
                    await db.LogAsync(LogSeverity.Info, LogEntry.Maintenance,
                        $"Journal système : {deleted} entrée(s) de plus de {settings.LogRetentionDays} jour(s) supprimée(s).", "Système", null);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Purge du journal système reportée.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
