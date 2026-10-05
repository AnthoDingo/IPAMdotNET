using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using IPAMdotNet.Networking;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace IPAMdotNet.Api;

/// <summary>
/// Objet exposé en lecture / écriture générique : propriétés modifiables, règles propres au type (unicité, normalisation),
/// et contrôle avant suppression (références à détacher, ou refus).
/// </summary>
internal sealed class ApiResource<T> where T : class
{
    public required string Path { get; init; }
    public required string Label { get; init; }

    /// <summary>Propriétés modifiables (noms C#) ; exposées en camelCase avec l'identifiant.</summary>
    public required string[] Fields { get; init; }

    /// <summary>Règles du formulaire : normalise l'objet et ajoute les erreurs (clé = champ JSON).</summary>
    public Func<AppDbContext, T, Dictionary<string, string[]>, Task>? Validate { get; init; }

    /// <summary>Avant suppression : renvoie un motif de refus (409), ou détache les références et renvoie null.</summary>
    public Func<AppDbContext, int, Task<string?>>? BeforeDelete { get; init; }

    /// <summary>Filtre de lecture selon les droits (sections) ; null = lisible par toute clé.</summary>
    public Func<IQueryable<T>, SectionAccess, IQueryable<T>>? ReadFilter { get; init; }
}

public static partial class ApiEndpoints
{
    private static readonly JsonSerializerOptions ValueOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static readonly NullabilityInfoContext Nullability = new();

    /// <summary>Objets d'administration (droits d'admin pour écrire), en lecture pour toute clé sauf restriction de section.</summary>
    private static void MapResources(RouteGroupBuilder api)
    {
        MapResource(api, new ApiResource<Section>
        {
            Path = "sections", Label = "Section", Fields = [nameof(Section.Name), nameof(Section.Description), nameof(Section.DefaultAccess)],
            ReadFilter = (query, access) => access.ReadableIds is { } ids ? query.Where(s => ids.Contains(s.Id)) : query,
            Validate = async (db, s, errors) =>
                AddIf(errors, await db.Sections.AnyAsync(x => x.Name == s.Name && x.Id != s.Id), "name", "Une section porte déjà ce nom."),
            BeforeDelete = async (db, id) =>
                await db.Subnets.AnyAsync(s => s.SectionId == id) ? "Impossible de supprimer une section qui contient des sous-réseaux." : null,
        });
        MapResource(api, new ApiResource<Location>
        {
            Path = "locations", Label = "Emplacement",
            Fields = [nameof(Location.Name), nameof(Location.Description), nameof(Location.Address), nameof(Location.Latitude), nameof(Location.Longitude)],
            Validate = (db, l, errors) =>
            {
                AddIf(errors, !Location.TryNormalizeCoordinate(l.Latitude, 90, out string? latitude), "latitude", "Latitude invalide (nombre entre -90 et 90).");
                AddIf(errors, !Location.TryNormalizeCoordinate(l.Longitude, 180, out string? longitude), "longitude", "Longitude invalide (nombre entre -180 et 180).");
                (l.Latitude, l.Longitude) = (latitude, longitude);
                return Task.CompletedTask;
            },
            BeforeDelete = async (db, id) => { await db.DetachLocationAsync(id); return null; },
        });
        MapResource(api, new ApiResource<Customer>
        {
            Path = "customers", Label = "Client",
            Fields = [nameof(Customer.Name), nameof(Customer.Address), nameof(Customer.PostCode), nameof(Customer.City), nameof(Customer.State),
                nameof(Customer.ContactPerson), nameof(Customer.ContactPhone), nameof(Customer.ContactMail), nameof(Customer.Note)],
            BeforeDelete = async (db, id) => { await db.DetachCustomerAsync(id); return null; },
        });
        MapResource(api, new ApiResource<Rack>
        {
            Path = "racks", Label = "Rack",
            Fields = [nameof(Rack.Name), nameof(Rack.Size), nameof(Rack.LocationId), nameof(Rack.CustomerId), nameof(Rack.Description)],
            Validate = async (db, r, errors) =>
            {
                int highest = r.Id == 0 ? 0 : await db.Devices.Where(d => d.RackId == r.Id && d.RackStart != null)
                    .Select(d => (int?)(d.RackStart + d.RackSize - 1)).MaxAsync() ?? 0;
                AddIf(errors, highest > r.Size, "size", $"Un équipement occupe l'unité {highest} : la hauteur ne peut pas être inférieure.");
            },
            BeforeDelete = async (db, id) => { await db.DetachRackAsync(id); return null; },
        });
        MapResource(api, new ApiResource<Nameserver>
        {
            Path = "nameservers", Label = "Serveurs de noms", Fields = [nameof(Nameserver.Name), nameof(Nameserver.Servers), nameof(Nameserver.Description)],
            Validate = (db, n, errors) =>
            {
                string[] servers = (n.Servers ?? "").Split([';', ',', ' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                string[] invalid = servers.Where(s => !System.Net.IPAddress.TryParse(s, out _)).ToArray();
                AddIf(errors, invalid.Length > 0, "servers", $"Adresse IP invalide : {string.Join(", ", invalid)}");
                n.Servers = string.Join(';', servers.Select(s => System.Net.IPAddress.TryParse(s, out System.Net.IPAddress? ip) ? ip.ToString() : s));
                return Task.CompletedTask;
            },
        });
        MapResource(api, new ApiResource<DeviceType>
        {
            Path = "device-types", Label = "Type d'équipement", Fields = [nameof(DeviceType.Name), nameof(DeviceType.Description)],
            Validate = async (db, t, errors) =>
                AddIf(errors, await db.DeviceTypes.AnyAsync(x => x.Name == t.Name && x.Id != t.Id), "name", "Ce type existe déjà."),
            BeforeDelete = async (db, id) => { await db.DetachDeviceTypeAsync(id); return null; },
        });
        MapResource(api, new ApiResource<CircuitProvider>
        {
            Path = "circuit-providers", Label = "Fournisseur",
            Fields = [nameof(CircuitProvider.Name), nameof(CircuitProvider.Contact), nameof(CircuitProvider.Description)],
            Validate = async (db, p, errors) =>
                AddIf(errors, await db.CircuitProviders.AnyAsync(x => x.Name == p.Name && x.Id != p.Id), "name", "Un fournisseur porte déjà ce nom."),
            BeforeDelete = async (db, id) =>
                await db.Circuits.AnyAsync(c => c.ProviderId == id) ? "Impossible de supprimer un fournisseur qui a des circuits." : null,
        });
        MapResource(api, new ApiResource<Circuit>
        {
            Path = "circuits", Label = "Circuit",
            Fields = [nameof(Circuit.Cid), nameof(Circuit.ProviderId), nameof(Circuit.Type), nameof(Circuit.Capacity), nameof(Circuit.Status),
                nameof(Circuit.LocationAId), nameof(Circuit.LocationBId), nameof(Circuit.CustomerId), nameof(Circuit.Comment)],
            Validate = async (db, c, errors) =>
                AddIf(errors, await db.Circuits.AnyAsync(x => x.ProviderId == c.ProviderId && x.Cid == c.Cid && x.Id != c.Id), "cid",
                    "Ce fournisseur a déjà un circuit avec cet identifiant."),
        });
        MapResource(api, new ApiResource<NatRule>
        {
            Path = "nat", Label = "Règle NAT",
            Fields = [nameof(NatRule.Name), nameof(NatRule.Type), nameof(NatRule.Source), nameof(NatRule.SourcePort), nameof(NatRule.Destination),
                nameof(NatRule.DestinationPort), nameof(NatRule.Description)],
            Validate = (db, r, errors) =>
            {
                if (Ip.TryNormalize(r.Source, out string source)) { r.Source = source; }
                else { AddIf(errors, true, "source", "Adresse ou réseau invalide (ex. 10.0.0.1 ou 10.0.0.0/24)."); }
                if (Ip.TryNormalize(r.Destination, out string destination)) { r.Destination = destination; }
                else { AddIf(errors, true, "destination", "Adresse ou réseau invalide (ex. 203.0.113.10 ou 203.0.113.0/28)."); }
                return Task.CompletedTask;
            },
        });
        MapResource(api, new ApiResource<BgpPeer>
        {
            Path = "bgp", Label = "Pair BGP",
            Fields = [nameof(BgpPeer.Name), nameof(BgpPeer.LocalAs), nameof(BgpPeer.LocalAddress), nameof(BgpPeer.PeerAs), nameof(BgpPeer.PeerAddress),
                nameof(BgpPeer.VrfId), nameof(BgpPeer.Description)],
            Validate = (db, p, errors) =>
            {
                p.LocalAddress = System.Net.IPAddress.TryParse(p.LocalAddress, out System.Net.IPAddress? local) ? local.ToString() : p.LocalAddress;
                p.PeerAddress = System.Net.IPAddress.TryParse(p.PeerAddress, out System.Net.IPAddress? peer) ? peer.ToString() : p.PeerAddress;
                AddIf(errors, local is null, "localAddress", "Adresse IP invalide.");
                AddIf(errors, peer is null, "peerAddress", "Adresse IP invalide.");
                return Task.CompletedTask;
            },
        });
        MapResource(api, new ApiResource<PstnPrefix>
        {
            Path = "pstn-prefixes", Label = "Préfixe RTC",
            Fields = [nameof(PstnPrefix.Prefix), nameof(PstnPrefix.Name), nameof(PstnPrefix.Start), nameof(PstnPrefix.Stop), nameof(PstnPrefix.DeviceId),
                nameof(PstnPrefix.Description)],
            Validate = async (db, p, errors) =>
            {
                string? normalized = PstnPrefix.Normalize(p.Prefix);
                AddIf(errors, normalized is null, "prefix", "Préfixe invalide : chiffres uniquement, « + » initial facultatif.");
                p.Prefix = normalized ?? p.Prefix;
                AddIf(errors, normalized is not null && await db.PstnPrefixes.AnyAsync(x => x.Prefix == normalized && x.Id != p.Id), "prefix", "Ce préfixe existe déjà.");
                AddIf(errors, p.Stop < p.Start, "stop", "Le dernier numéro doit être supérieur ou égal au premier.");
                AddIf(errors, p.Id != 0 && await db.PstnNumbers.AnyAsync(n => n.PrefixId == p.Id && (n.Number < p.Start || n.Number > p.Stop)), "stop",
                    "Des numéros existants sortiraient de la plage.");
            },
        });
        MapResource(api, new ApiResource<PstnNumber>
        {
            Path = "pstn-numbers", Label = "Numéro RTC",
            Fields = [nameof(PstnNumber.PrefixId), nameof(PstnNumber.Number), nameof(PstnNumber.Name), nameof(PstnNumber.Owner), nameof(PstnNumber.State),
                nameof(PstnNumber.DeviceId), nameof(PstnNumber.Description)],
            Validate = async (db, n, errors) =>
            {
                PstnPrefix? prefix = await db.PstnPrefixes.FindAsync(n.PrefixId);
                AddIf(errors, prefix is not null && (n.Number < prefix.Start || n.Number > prefix.Stop), "number",
                    $"Le numéro doit être compris entre {prefix?.Start} et {prefix?.Stop}.");
                AddIf(errors, await db.PstnNumbers.AnyAsync(x => x.PrefixId == n.PrefixId && x.Number == n.Number && x.Id != n.Id), "number",
                    "Ce numéro existe déjà dans le préfixe.");
            },
        });
    }

    private static void MapResource<T>(RouteGroupBuilder api, ApiResource<T> resource) where T : class, new()
    {
        string entityType = typeof(T).Name;

        api.MapGet($"/{resource.Path}", async (HttpContext http, AppDbContext db) =>
        {
            List<T> items = await Readable(db, resource, Context(http).Access).ToListAsync();
            Dictionary<int, Dictionary<string, string>> custom = await CustomFieldsByIdAsync(db, entityType);
            return Results.Ok(items.Select(item => ToJson(resource, item, custom)).OrderBy(i => (int)i["id"]!));
        });

        api.MapGet($"/{resource.Path}/{{id:int}}", async (HttpContext http, AppDbContext db, int id) =>
        {
            T? item = await Readable(db, resource, Context(http).Access).SingleOrDefaultAsync(e => EF.Property<int>(e, "Id") == id);
            return item is null ? NotFound(resource.Label, id) : Results.Ok(ToJson(resource, item, await CustomFieldsByIdAsync(db, entityType)));
        });

        api.MapPost($"/{resource.Path}", async (HttpContext http, AppDbContext db, JsonElement body) =>
        {
            if (AdminWriteDenied(Context(http)) is { } denied)
            {
                return denied;
            }
            T item = new();
            return await SaveAsync(http, db, resource, item, body, creating: true);
        });

        api.MapPatch($"/{resource.Path}/{{id:int}}", async (HttpContext http, AppDbContext db, int id, JsonElement body) =>
        {
            if (AdminWriteDenied(Context(http)) is { } denied)
            {
                return denied;
            }
            T? item = await db.Set<T>().FindAsync(id);
            return item is null ? NotFound(resource.Label, id) : await SaveAsync(http, db, resource, item, body, creating: false);
        });

        api.MapDelete($"/{resource.Path}/{{id:int}}", async (HttpContext http, AppDbContext db, int id) =>
        {
            if (AdminWriteDenied(Context(http)) is { } denied)
            {
                return denied;
            }
            T? item = await db.Set<T>().FindAsync(id);
            if (item is null)
            {
                return NotFound(resource.Label, id);
            }
            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync();
            if (resource.BeforeDelete is not null && await resource.BeforeDelete(db, id) is { } refusal)
            {
                return Results.Conflict(new { error = refusal });
            }
            db.Remove(item);
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                return Results.Conflict(new { error = $"{resource.Label} {id} est encore utilisé par d'autres objets." });
            }
            await transaction.CommitAsync();
            return Results.NoContent();
        });
    }

    private static IQueryable<T> Readable<T>(AppDbContext db, ApiResource<T> resource, SectionAccess access) where T : class =>
        resource.ReadFilter is null ? db.Set<T>().AsNoTracking() : resource.ReadFilter(db.Set<T>().AsNoTracking(), access);

    /// <summary>Applique le corps JSON (champs présents seulement), valide, enregistre, puis enregistre les champs personnalisés.</summary>
    private static async Task<IResult> SaveAsync<T>(HttpContext http, AppDbContext db, ApiResource<T> resource, T item, JsonElement body, bool creating)
        where T : class
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            return Invalid("", "Objet JSON attendu.");
        }
        Dictionary<string, string[]> errors = [];
        Dictionary<string, JsonElement> custom = [];
        foreach (JsonProperty property in body.EnumerateObject())
        {
            if (string.Equals(property.Name, "customFields", StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    custom = property.Value.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                }
                continue;
            }
            if (string.Equals(property.Name, "id", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            PropertyInfo? target = resource.Fields.Select(f => typeof(T).GetProperty(f)!)
                .FirstOrDefault(p => string.Equals(p.Name, property.Name, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                errors.TryAdd(property.Name, ["Champ inconnu ou non modifiable."]);
                continue;
            }
            if (!TryConvert(target, property.Value, out object? value))
            {
                errors.TryAdd(JsonName(target.Name), ["Valeur invalide."]);
                continue;
            }
            target.SetValue(item, value);
        }
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        await CheckForeignKeysAsync(db, item, errors);
        if (resource.Validate is not null)
        {
            await resource.Validate(db, item, errors);
        }
        foreach (KeyValuePair<string, string[]> error in Validate(item))
        {
            errors.TryAdd(error.Key, error.Value);
        }
        (Dictionary<int, string?> customValues, Dictionary<string, string[]> customErrors) = await PrepareCustomFieldsAsync(db, typeof(T).Name, custom, creating);
        foreach (KeyValuePair<string, string[]> error in customErrors)
        {
            errors.TryAdd(error.Key, error.Value);
        }
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        if (creating)
        {
            db.Add(item);
        }
        await db.SaveChangesAsync();
        int id = (int)db.Entry(item).Property("Id").CurrentValue!;
        if (customValues.Count > 0)
        {
            await CustomFieldForm.SaveAsync(db, id, customValues);
        }
        Dictionary<string, object?> json = ToJson(resource, item, await CustomFieldsByIdAsync(db, typeof(T).Name));
        return creating ? Results.Created($"/api/{resource.Path}/{id}", json) : Results.Ok(json);
    }

    /// <summary>Valeur JSON vers le type de la propriété ; chaîne vide = null si la propriété l'accepte, identifiant 0 = aucune référence.</summary>
    private static bool TryConvert(PropertyInfo property, JsonElement json, out object? value)
    {
        bool nullable = Nullability.Create(property).WriteState == NullabilityState.Nullable;
        value = null;
        try
        {
            if (property.PropertyType == typeof(string))
            {
                string? text = json.ValueKind == JsonValueKind.Null ? null : json.ValueKind == JsonValueKind.String ? json.GetString()?.Trim() : json.GetRawText();
                value = string.IsNullOrEmpty(text) ? (nullable ? null : "") : text;
                return true;
            }
            value = json.Deserialize(property.PropertyType, ValueOptions);
            if (value is Enum member && !Enum.IsDefined(member.GetType(), member))
            {
                return false;
            }
            if (value is 0 && property.PropertyType == typeof(int?))
            {
                value = null;
            }
            return value is not null || nullable || Nullable.GetUnderlyingType(property.PropertyType) is not null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    /// <summary>Références (emplacement, client, fournisseur…) : l'objet visé doit exister.</summary>
    private static async Task CheckForeignKeysAsync<T>(AppDbContext db, T item, Dictionary<string, string[]> errors) where T : class
    {
        IEntityType entity = db.Model.FindEntityType(typeof(T))!;
        foreach (IForeignKey foreignKey in entity.GetForeignKeys().Where(k => k.Properties.Count == 1 && k.Properties[0].PropertyInfo is not null))
        {
            object? value = foreignKey.Properties[0].PropertyInfo!.GetValue(item);
            if (value is not null && await db.FindAsync(foreignKey.PrincipalEntityType.ClrType, value) is null)
            {
                errors.TryAdd(JsonName(foreignKey.Properties[0].Name), ["Objet référencé inexistant."]);
            }
        }
    }

    private static Dictionary<string, object?> ToJson<T>(ApiResource<T> resource, T item, Dictionary<int, Dictionary<string, string>> custom)
        where T : class
    {
        int id = (int)typeof(T).GetProperty("Id")!.GetValue(item)!;
        Dictionary<string, object?> json = new() { ["id"] = id };
        foreach (string field in resource.Fields)
        {
            object? value = typeof(T).GetProperty(field)!.GetValue(item);
            json[JsonName(field)] = value is Enum member ? member.ToString() : value;
        }
        if (CustomField.SupportedTypes.Contains(typeof(T).Name))
        {
            json["customFields"] = custom.GetValueOrDefault(id) ?? [];
        }
        return json;
    }

    /// <summary>
    /// Valeurs de champs personnalisés envoyées (nom → valeur JSON) : validées comme dans les formulaires. En création, tous les
    /// champs du type le sont (obligatoires compris) ; en modification, seulement ceux envoyés.
    /// </summary>
    private static async Task<(Dictionary<int, string?> Values, Dictionary<string, string[]> Errors)> PrepareCustomFieldsAsync(AppDbContext db,
        string entityType, IReadOnlyDictionary<string, JsonElement>? posted, bool creating)
    {
        Dictionary<string, string[]> errors = [];
        if (!CustomField.SupportedTypes.Contains(entityType))
        {
            AddIf(errors, posted is { Count: > 0 }, "customFields", "Ce type d'objet n'a pas de champs personnalisés.");
            return ([], errors);
        }
        List<CustomField> fields = await CustomFieldForm.DefinitionsAsync(db, entityType);
        Dictionary<int, string?> raw = [];
        foreach (KeyValuePair<string, JsonElement> pair in posted ?? new Dictionary<string, JsonElement>())
        {
            CustomField? field = fields.FirstOrDefault(f => string.Equals(f.Name, pair.Key, StringComparison.OrdinalIgnoreCase));
            if (field is null)
            {
                errors.TryAdd($"customFields.{pair.Key}", ["Champ personnalisé inconnu."]);
                continue;
            }
            raw[field.Id] = pair.Value.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => pair.Value.GetString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => pair.Value.GetRawText(),
            };
        }
        ModelStateDictionary state = new();
        Dictionary<int, string?> values = CustomFieldForm.Validate(creating ? fields : fields.Where(f => raw.ContainsKey(f.Id)).ToList(), raw, state);
        foreach (KeyValuePair<string, ModelStateEntry> entry in state)
        {
            if (entry.Value is not { Errors.Count: > 0 } stateEntry)
            {
                continue;
            }
            // Clé « Custom[12] » → nom du champ.
            string name = fields.FirstOrDefault(f => entry.Key == $"{CustomFieldForm.Prefix}[{f.Id}]")?.Name ?? entry.Key;
            errors.TryAdd($"customFields.{name}", stateEntry.Errors.Select(e => e.ErrorMessage).ToArray());
        }
        return (values, errors);
    }

    private static async Task SaveCustomFieldsAsync(AppDbContext db, int entityId, Dictionary<int, string?> values)
    {
        if (values.Count > 0)
        {
            await CustomFieldForm.SaveAsync(db, entityId, values);
        }
    }

    /// <summary>Champs personnalisés d'un objet (nom → valeur).</summary>
    private static async Task<Dictionary<string, string>> CustomOfAsync(AppDbContext db, string entityType, int entityId) =>
        await db.CustomFieldValues.Where(v => v.EntityId == entityId && v.Field!.EntityType == entityType)
            .ToDictionaryAsync(v => v.Field!.Name, v => v.Value);

    /// <summary>Champs personnalisés d'un type : objet → (nom du champ → valeur).</summary>
    private static async Task<Dictionary<int, Dictionary<string, string>>> CustomFieldsByIdAsync(AppDbContext db, string entityType)
    {
        if (!CustomField.SupportedTypes.Contains(entityType))
        {
            return [];
        }
        Dictionary<int, string> names = (await CustomFieldForm.DefinitionsAsync(db, entityType)).ToDictionary(f => f.Id, f => f.Name);
        return (await CustomFieldForm.ValuesForAsync(db, entityType))
            .ToDictionary(p => p.Key, p => p.Value.Where(v => names.ContainsKey(v.Key)).ToDictionary(v => names[v.Key], v => v.Value));
    }
}
