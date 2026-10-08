using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using IPAMdotNet.Maintenance;
using IPAMdotNet.Networking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

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

    private Func<ClaimsPrincipal?>? auditUser;

    /// <summary>
    /// L'utilisateur est lu à l'enregistrement, pas à la création du contexte : celui-ci peut être résolu
    /// par un middleware avant l'authentification, l'utilisateur serait alors encore anonyme (« Système »).
    /// </summary>
    public AppDbContext WithAuditUser(Func<ClaimsPrincipal?> user)
    {
        auditUser = user;
        return this;
    }

    private void ResolveAuditUser()
    {
        ClaimsPrincipal? user = auditUser?.Invoke();
        if (user?.Identity?.IsAuthenticated == true)
        {
            AuditUserId = int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out int id) ? id : null;
            AuditUserName = user.Identity.Name ?? "";
        }
    }

    /// <summary>Import en masse (phpIPAM) : pas de journal des modifications, un résumé va dans le journal système.</summary>
    public bool SuppressAudit { get; set; }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        if (SuppressAudit)
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        List<PendingChange> pending = await CollectChangesAsync(cancellationToken);
        await CollectCustomValueChangesAsync(pending, cancellationToken);
        // Numéros RTC supprimés en cascade côté base avec leur préfixe : leurs valeurs (sans clé étrangère) d'abord.
        List<int> deletedPrefixes = pending.Where(p => p.Action == ChangeAction.Deleted && p.Entry.Entity is PstnPrefix)
            .Select(p => ((PstnPrefix)p.Entry.Entity).Id).ToList();
        if (deletedPrefixes.Count > 0)
        {
            await CustomFieldValues.Where(v => v.Field!.EntityType == nameof(PstnNumber)
                && PstnNumbers.Any(n => n.Id == v.EntityId && deletedPrefixes.Contains(n.PrefixId))).ExecuteDeleteAsync(cancellationToken);
        }
        // Avant l'enregistrement : un sous-réseau supprimé en même temps est encore lisible.
        Dictionary<int, int> subnetSections = await SubnetSectionsAsync(pending, cancellationToken);
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

        if (!(await SettingsStore.GetServerAsync(this)).EnableChangelog)
        {
            return result;
        }

        // Après l'enregistrement : les identifiants des objets créés sont connus.
        // ponytail: deux SaveChanges sans transaction, une panne entre les deux perd l'entrée du journal (pas la donnée).
        DateTime now = DateTime.UtcNow;
        ResolveAuditUser();
        foreach (PendingChange change in pending)
        {
            int entityId = change.Entry.Metadata.FindProperty("Id") is null ? 0 : (int)(change.Entry.Property("Id").CurrentValue ?? 0);
            ChangeLogs.Add(new ChangeLog
            {
                SectionId = SectionOf(change, entityId, subnetSections),
                Date = now,
                UserId = AuditUserId,
                UserName = AuditUserName,
                EntityType = change.Entry.Metadata.ClrType.Name,
                EntityId = entityId,
                EntityLabel = Label(change.Entry, change.Action == ChangeAction.Deleted),
                Action = change.Action,
                Changes = change.Changes.Count == 0 ? null : JsonSerializer.Serialize(change.Changes, JsonOptions),
            });
        }
        await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        return result;
    }

    /// <summary>Section des sous-réseaux auxquels appartiennent les adresses et demandes modifiées.</summary>
    private async Task<Dictionary<int, int>> SubnetSectionsAsync(List<PendingChange> pending, CancellationToken cancellationToken)
    {
        List<int> subnetIds = pending.Where(p => p.Entry.Entity is IpAddress or IpRequest)
            .Select(p => (int)(p.Entry.Property("SubnetId").CurrentValue ?? p.Entry.Property("SubnetId").OriginalValue ?? 0))
            .Distinct().ToList();
        return subnetIds.Count == 0 ? [] : await Subnets.AsNoTracking().Where(s => subnetIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.SectionId, cancellationToken);
    }

    private static int? SectionOf(PendingChange change, int entityId, Dictionary<int, int> subnetSections)
    {
        EntityEntry entry = change.Entry;
        object? Value(string name) => change.Action == ChangeAction.Deleted ? entry.Property(name).OriginalValue : entry.Property(name).CurrentValue;
        return entry.Entity switch
        {
            Section => entityId,
            Subnet => (int?)Value(nameof(Subnet.SectionId)),
            IpAddress or IpRequest => Value("SubnetId") is int subnetId && subnetSections.TryGetValue(subnetId, out int sectionId) ? sectionId : null,
            _ => null,
        };
    }

    private async Task<List<PendingChange>> CollectChangesAsync(CancellationToken cancellationToken)
    {
        List<PendingChange> pending = [];
        // Libellés des objets référencés, résolus une fois par enregistrement (ajout en masse : même sous-réseau, même étiquette).
        Dictionary<(Type, object), string?> labels = [];
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
                        string? value = await FormatAsync(entry, property, property.CurrentValue, labels, cancellationToken);
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
                        string? value = await FormatAsync(entry, property, property.OriginalValue, labels, cancellationToken);
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
                        string? before = await FormatAsync(entry, property, database?[property.Metadata.Name], labels, cancellationToken);
                        string? after = await FormatAsync(entry, property, property.CurrentValue, labels, cancellationToken);
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

    /// <summary>
    /// Valeurs de champs personnalisés : journalisées comme une modification de leur objet (libellé = nom du champ),
    /// fusionnées avec son entrée si l'objet est enregistré dans la même opération.
    /// </summary>
    private async Task CollectCustomValueChangesAsync(List<PendingChange> pending, CancellationToken cancellationToken)
    {
        List<EntityEntry<CustomFieldValue>> entries = ChangeTracker.Entries<CustomFieldValue>()
            .Where(e => e.State is EntityState.Added or EntityState.Deleted or EntityState.Modified).ToList();
        if (entries.Count == 0)
        {
            return;
        }
        List<int> fieldIds = entries.Select(e => e.Entity.FieldId).Distinct().ToList();
        Dictionary<int, CustomField> fields = await CustomFields.AsNoTracking().Where(f => fieldIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, cancellationToken);
        foreach (IGrouping<(string Type, int Id), EntityEntry<CustomFieldValue>> group in entries.Where(e => fields.ContainsKey(e.Entity.FieldId))
            .GroupBy(e => (fields[e.Entity.FieldId].EntityType, e.Entity.EntityId)))
        {
            Dictionary<string, string?[]> changes = [];
            foreach (EntityEntry<CustomFieldValue> entry in group)
            {
                CustomField field = fields[entry.Entity.FieldId];
                string? before = entry.State == EntityState.Added ? null : FormatCustomValue(field, entry.Property(v => v.Value).OriginalValue);
                string? after = entry.State == EntityState.Deleted ? null : FormatCustomValue(field, entry.Entity.Value);
                if (before != after)
                {
                    changes[field.Name] = [before, after];
                }
            }
            if (changes.Count == 0)
            {
                continue;
            }
            bool IsOwner(EntityEntry e) => e.Metadata.ClrType.Name == group.Key.Type && e.Metadata.FindProperty("Id") is not null
                && Equals(e.Property("Id").CurrentValue, group.Key.Id);
            if (pending.FirstOrDefault(p => p.Action != ChangeAction.Deleted && IsOwner(p.Entry)) is { } owner)
            {
                foreach (KeyValuePair<string, string?[]> change in changes)
                {
                    owner.Changes[change.Key] = change.Value;
                }
                continue;
            }
            EntityEntry? tracked = ChangeTracker.Entries().FirstOrDefault(IsOwner);
            object? entity = tracked?.Entity;
            if (entity is null && Model.GetEntityTypes().FirstOrDefault(t => t.ClrType.Name == group.Key.Type) is { } entityType)
            {
                entity = await (Task<object?>)typeof(AppDbContext)
                    .GetMethod(nameof(FindUntrackedAsync), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(entityType.ClrType, typeof(int))
                    .Invoke(this, ["Id", group.Key.Id, cancellationToken])!;
            }
            if (entity is not null)
            {
                // Objet non suivi : Entry() ne l'attache pas, il sert seulement au libellé et à la section.
                pending.Add(new PendingChange(tracked ?? Entry(entity), ChangeAction.Updated, changes));
            }
        }
    }

    private static string? FormatCustomValue(CustomField field, string? value) => string.IsNullOrEmpty(value) ? null
        : field.Type == CustomFieldType.Boolean ? (value == "true" ? "Oui" : "Non") : value;

    private static string FieldLabel(IProperty property) =>
        property.PropertyInfo?.GetCustomAttribute<DisplayAttribute>()?.Name ?? property.Name;

    private static string FieldLabel(PropertyEntry property) => FieldLabel(property.Metadata);

    /// <summary>
    /// Comme <see cref="Format"/>, mais une clé étrangère est écrite avec le libellé de l'objet référencé au moment du changement
    /// (« Serveurs (n°3) » plutôt que « 3 ») : le journal reste lisible même si l'objet est renommé ou supprimé ensuite.
    /// </summary>
    private async Task<string?> FormatAsync(EntityEntry entry, PropertyEntry property, object? value, Dictionary<(Type, object), string?> labels,
        CancellationToken cancellationToken)
    {
        string? text = Format(entry, property, value);
        if (text is null || property.Metadata.GetContainingForeignKeys().FirstOrDefault() is not { } foreignKey
            || foreignKey.PrincipalKey.Properties.Count != 1)
        {
            return text;
        }
        string? label = await ReferenceLabelAsync(foreignKey, value!, labels, cancellationToken);
        return label is null ? text : $"{label} (n°{text})";
    }

    /// <summary>Libellé de l'objet visé par une clé étrangère (null s'il n'existe pas), mis en cache pour l'opération en cours.</summary>
    private async Task<string?> ReferenceLabelAsync(IForeignKey foreignKey, object value, Dictionary<(Type, object), string?> labels,
        CancellationToken cancellationToken)
    {
        Type principalType = foreignKey.PrincipalEntityType.ClrType;
        if (!labels.TryGetValue((principalType, value), out string? label))
        {
            IProperty keyProperty = foreignKey.PrincipalKey.Properties[0];
            EntityEntry? local = ChangeTracker.Entries()
                .FirstOrDefault(e => e.Metadata.ClrType == principalType && Equals(e.Property(keyProperty.Name).CurrentValue, value));
            // Lu sans suivi : charger puis détacher l'objet détacherait aussi ses dépendants en cours d'ajout (cascade).
            object? principal = local?.Entity ?? await (Task<object?>)typeof(AppDbContext)
                .GetMethod(nameof(FindUntrackedAsync), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(principalType, keyProperty.ClrType)
                .Invoke(this, [keyProperty.Name, value, cancellationToken])!;
            label = principal is null ? null : Label(Entry(principal), original: false);
            labels[(principalType, value)] = label;
        }
        return label;
    }

    /// <summary>
    /// Reprise des entrées écrites avant la résolution des libellés : une clé étrangère notée par son seul identifiant
    /// (« 3 ») devient « Serveurs (n°3) ». Un objet supprimé depuis garde son identifiant. Renvoie le nombre d'entrées modifiées.
    /// </summary>
    public async Task<int> BackfillChangeLogLabelsAsync(CancellationToken cancellationToken)
    {
        // Par type journalisé : libellé du champ (comme dans les entrées) → clé étrangère.
        Dictionary<string, Dictionary<string, IForeignKey>> foreignKeys = Model.GetEntityTypes()
            .Where(e => ChangeLog.Types.ContainsKey(e.ClrType.Name))
            .ToDictionary(e => e.ClrType.Name, e => e.GetForeignKeys()
                .Where(k => k.Properties.Count == 1 && k.PrincipalKey.Properties.Count == 1 && (Nullable.GetUnderlyingType(k.Properties[0].ClrType) ?? k.Properties[0].ClrType) == typeof(int))
                .GroupBy(k => FieldLabel(k.Properties[0])).ToDictionary(g => g.Key, g => g.First()));
        Dictionary<(Type, object), string?> labels = [];
        int updated = 0;
        int lastId = 0;
        while (true)
        {
            List<ChangeLog> batch = await ChangeLogs.Where(c => c.Id > lastId && c.Changes != null).OrderBy(c => c.Id).Take(500).ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }
            foreach (ChangeLog log in batch)
            {
                if (!foreignKeys.TryGetValue(log.EntityType, out Dictionary<string, IForeignKey>? fields) || fields.Count == 0)
                {
                    continue;
                }
                Dictionary<string, string?[]>? changes = JsonSerializer.Deserialize<Dictionary<string, string?[]>>(log.Changes!);
                bool changed = false;
                foreach (KeyValuePair<string, string?[]> change in changes ?? [])
                {
                    if (!fields.TryGetValue(change.Key, out IForeignKey? foreignKey))
                    {
                        continue;
                    }
                    for (int i = 0; i < change.Value.Length; i++)
                    {
                        // Seules les valeurs purement numériques sont d'anciennes entrées ; « Nom (n°3) » est déjà résolu.
                        if (change.Value[i] is { Length: > 0 } text && text.All(char.IsAsciiDigit) && int.TryParse(text, out int id)
                            && await ReferenceLabelAsync(foreignKey, id, labels, cancellationToken) is { } label)
                        {
                            change.Value[i] = $"{label} (n°{text})";
                            changed = true;
                        }
                    }
                }
                if (changed)
                {
                    log.Changes = JsonSerializer.Serialize(changes, JsonOptions);
                    updated++;
                }
            }
            await base.SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
            lastId = batch[^1].Id;
            ChangeTracker.Clear();
        }
        return updated;
    }

    /// <summary>Objet par sa clé, sans suivi (libellé du journal).</summary>
    private async Task<object?> FindUntrackedAsync<TEntity, TKey>(string keyName, TKey key, CancellationToken cancellationToken) where TEntity : class
    {
        ParameterExpression entity = Expression.Parameter(typeof(TEntity), "e");
        Expression<Func<TEntity, bool>> predicate = Expression.Lambda<Func<TEntity, bool>>(
            Expression.Equal(
                Expression.Call(typeof(EF), nameof(EF.Property), [typeof(TKey)], entity, Expression.Constant(keyName)),
                Expression.Constant(key, typeof(TKey))),
            entity);
        return await Set<TEntity>().AsNoTracking().FirstOrDefaultAsync(predicate, cancellationToken);
    }

    private static string? Format(EntityEntry entry, PropertyEntry property, object? value)
    {
        bool secret = entry.Entity switch
        {
            User => property.Metadata.Name == nameof(User.PasswordHash),
            AppSetting setting => property.Metadata.Name == nameof(AppSetting.Value) && setting.Key.EndsWith("Password", StringComparison.Ordinal),
            ApiKey => property.Metadata.Name == nameof(ApiKey.KeyHash),
            _ => false,
        };
        if (secret)
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
        if (entry.Entity is IpAddress && Value(nameof(IpAddress.Address)) is byte[] host)
        {
            return Ip.FromBytes(host).ToString();
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
