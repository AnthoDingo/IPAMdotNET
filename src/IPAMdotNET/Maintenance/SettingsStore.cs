using System.Globalization;
using System.Reflection;
using IPAMdotNet.Data;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Maintenance;

/// <summary>
/// Lecture / écriture des objets de paramètres (une ligne <see cref="AppSetting"/> par propriété : « Préfixe.Propriété »)
/// et cache en mémoire des paramètres serveur, lus à chaque requête (titre, fonctionnalités, sécurité).
/// </summary>
public static class SettingsStore
{
    public const string ServerPrefix = "Server";
    public const string MailPrefix = "Mail";
    public const string WidgetsPrefix = "Widgets";
    public const string ScanPrefix = "Scan";

    // Cache par processus : rechargé à l'enregistrement et relu au plus toutes les RefreshInterval, pour que les autres
    // instances (multi-instance derrière un répartiteur) voient un changement sans redémarrage.
    private static ServerSettings? server;
    private static DateTime loadedAt;

    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    /// <summary>Paramètres serveur en cache (valeurs par défaut tant qu'ils n'ont pas été chargés).</summary>
    public static ServerSettings Server => server ?? new ServerSettings();

    public static async Task<ServerSettings> GetServerAsync(AppDbContext db)
    {
        if (server is null || DateTime.UtcNow - loadedAt > RefreshInterval)
        {
            server = await LoadAsync<ServerSettings>(db, ServerPrefix);
            loadedAt = DateTime.UtcNow;
        }
        return server;
    }

    public static async Task<T> LoadAsync<T>(AppDbContext db, string prefix) where T : new()
    {
        string start = prefix + ".";
        Dictionary<string, string?> values = await db.AppSettings.Where(s => s.Key.StartsWith(start))
            .ToDictionaryAsync(s => s.Key[start.Length..], s => s.Value);
        T settings = new();
        foreach (PropertyInfo property in Properties<T>())
        {
            if (values.TryGetValue(property.Name, out string? text) && text is not null)
            {
                Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                property.SetValue(settings, type.IsEnum
                    ? (Enum.TryParse(type, text, out object? value) ? value : Activator.CreateInstance(type))
                    : Convert.ChangeType(text, type, CultureInfo.InvariantCulture));
            }
        }
        return settings;
    }

    public static async Task SaveAsync<T>(AppDbContext db, string prefix, T settings)
    {
        foreach (PropertyInfo property in Properties<T>())
        {
            string key = $"{prefix}.{property.Name}";
            string? value = Convert.ToString(property.GetValue(settings), CultureInfo.InvariantCulture);
            AppSetting? existing = await db.AppSettings.FindAsync(key);
            if (existing is null)
            {
                db.AppSettings.Add(new AppSetting { Key = key, Value = value });
            }
            else if (existing.Value != value)
            {
                existing.Value = value;
            }
        }
        await db.SaveChangesAsync();
        if (settings is ServerSettings saved)
        {
            server = saved;
            loadedAt = DateTime.UtcNow;
        }
    }

    private static IEnumerable<PropertyInfo> Properties<T>() =>
        typeof(T).GetProperties().Where(p => p.CanWrite && p.CanRead);
}
