using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using IPAMdotNet.Networking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace IPAMdotNet.Data;

/// <summary>
/// Journal des modifications : chaque SaveChanges enregistre qui a créé, modifié ou supprimé quoi.
/// Les ExecuteUpdate / ExecuteDelete (Detach*Async, ré-hachage du mot de passe) ne sont volontairement pas journalisés.
/// </summary>
public abstract partial class AppDbContext
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly string[] LabelProperties = ["Name", "Hostname", "Cid", "Prefix", "UserName", "Key", "Number", "Description"];

    public int? AuditUserId { get; set; }
    public string AuditUserName { get; set; } = "Système";

    private sealed record PendingChange(EntityEntry Entry, ChangeAction Action, Dictionary<string, string?[]> Changes);

    public AppDbContext WithAuditUser(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated == true)
        {
            AuditUserId = int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out int id) ? id : null;
            AuditUserName = user.Identity.Name ?? "";
        }
        return this;
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        List<PendingChange> pending = await CollectChangesAsync(cancellationToken);
        int result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if (pending.Count == 0)
        {
            return result;
        }

        // Les valeurs de champs personnalisés n'ont pas de clé étrangère vers leur objet : on les supprime avec lui.
        foreach (PendingChange deleted in pending.Where(p => p.Action == ChangeAction.Deleted))
        {
            string type = deleted.Entry.Metadata.ClrType.Name;
            if (CustomField.SupportedTypes.Contains(type))
            {
                int id = (int)(deleted.Entry.Property("Id").OriginalValue ?? 0);
                await CustomFieldValues.Where(v => v.Field!.EntityType == type && v.EntityId == id).ExecuteDeleteAsync(cancellationToken);
            }
        }

        // Après l'enregistrement : les identifiants des objets créés sont connus.
        // ponytail: deux SaveChanges sans transaction, une panne entre les deux perd l'entrée du journal (pas la donnée).
        DateTime now = DateTime.UtcNow;
        foreach (PendingChange change in pending)
        {
            ChangeLogs.Add(new ChangeLog
            {
                Date = now,
                UserId = AuditUserId,
                UserName = AuditUserName,
                EntityType = change.Entry.Metadata.ClrType.Name,
                EntityId = change.Entry.Metadata.FindProperty("Id") is null ? 0 : (int)(change.Entry.Property("Id").CurrentValue ?? 0),
                EntityLabel = Label(change.Entry, change.Action == ChangeAction.Deleted),
                Action = change.Action,
                Changes = change.Changes.Count == 0 ? null : JsonSerializer.Serialize(change.Changes, JsonOptions),
            });
        }
        await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        return result;
    }

    private async Task<List<PendingChange>> CollectChangesAsync(CancellationToken cancellationToken)
    {
        List<PendingChange> pending = [];
        foreach (EntityEntry entry in ChangeTracker.Entries().ToList())
        {
            if (!ChangeLog.Types.ContainsKey(entry.Metadata.ClrType.Name))
            {
                continue;
            }
            Dictionary<string, string?[]> changes = [];
            switch (entry.State)
            {
                case EntityState.Added:
                    foreach (PropertyEntry property in entry.Properties.Where(p => !p.Metadata.IsPrimaryKey()))
                    {
                        string? value = Format(entry, property, property.CurrentValue);
                        if (value is not null)
                        {
                            changes[FieldLabel(property)] = [null, value];
                        }
                    }
                    pending.Add(new PendingChange(entry, ChangeAction.Created, changes));
                    break;

                case EntityState.Deleted:
                    foreach (PropertyEntry property in entry.Properties.Where(p => !p.Metadata.IsPrimaryKey()))
                    {
                        string? value = Format(entry, property, property.OriginalValue);
                        if (value is not null)
                        {
                            changes[FieldLabel(property)] = [value, null];
                        }
                    }
                    pending.Add(new PendingChange(entry, ChangeAction.Deleted, changes));
                    break;

                case EntityState.Modified:
                    // Les pages font db.Update(objet détaché) : les valeurs d'origine ne sont connues que de la base.
                    PropertyValues? database = await entry.GetDatabaseValuesAsync(cancellationToken);
                    foreach (PropertyEntry property in entry.Properties.Where(p => p.IsModified))
                    {
                        string? before = Format(entry, property, database?[property.Metadata.Name]);
                        string? after = Format(entry, property, property.CurrentValue);
                        if (before != after)
                        {
                            changes[FieldLabel(property)] = [before, after];
                        }
                    }
                    if (changes.Count > 0)
                    {
                        pending.Add(new PendingChange(entry, ChangeAction.Updated, changes));
                    }
                    break;
            }
        }
        return pending;
    }

    private static string FieldLabel(PropertyEntry property) =>
        property.Metadata.PropertyInfo?.GetCustomAttribute<DisplayAttribute>()?.Name ?? property.Metadata.Name;

    private static string? Format(EntityEntry entry, PropertyEntry property, object? value)
    {
        if (entry.Entity is User && property.Metadata.Name == nameof(User.PasswordHash))
        {
            return value is null ? null : "***";
        }
        return value switch
        {
            null => null,
            byte[] { Length: 16 } bytes => Ip.FromBytes(bytes).ToString(),
            bool flag => flag ? "Oui" : "Non",
            Enum member => member.GetType().GetField(member.ToString())?.GetCustomAttribute<DisplayAttribute>()?.Name ?? member.ToString(),
            DateTime date => date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            string text => text.Length == 0 ? null : text,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
    }

    private static string? Label(EntityEntry entry, bool original)
    {
        object? Value(string name) => entry.Metadata.FindProperty(name) is null ? null
            : original ? entry.Property(name).OriginalValue : entry.Property(name).CurrentValue;

        if (entry.Entity is Subnet && Value(nameof(Subnet.Address)) is byte[] address)
        {
            return $"{Ip.FromBytes(address)}/{Value(nameof(Subnet.PrefixLength))}";
        }
        foreach (string name in LabelProperties)
        {
            if (Convert.ToString(Value(name), CultureInfo.InvariantCulture) is { Length: > 0 } text)
            {
                return text.Length > 200 ? text[..199] + "…" : text;
            }
        }
        return null;
    }
}
