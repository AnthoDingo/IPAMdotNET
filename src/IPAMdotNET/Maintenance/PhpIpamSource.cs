using System.Data.Common;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MySql.Data.MySqlClient;

namespace IPAMdotNet.Maintenance;

/// <summary>Colonne personnalisée phpIPAM (« custom_* » ajoutée par ALTER TABLE).</summary>
public sealed record PhpIpamCustomField(string Table, string Column, string SqlType, string? Comment);

/// <summary>
/// Données lues d'une instance phpIPAM, au format des tables de sa base (noms de tables et de colonnes de phpIPAM),
/// quelle que soit la source : base MySQL (complète) ou API REST (partielle : ni utilisateurs, ni groupes, ni paramètres).
/// </summary>
public sealed class PhpIpamData
{
    public Dictionary<string, List<Dictionary<string, string?>>> Tables { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<PhpIpamCustomField> CustomFields { get; } = [];
    public List<string> Warnings { get; } = [];

    /// <summary>Source complète (base de données) : utilisateurs, groupes et permissions sont importés.</summary>
    public bool FromDatabase { get; init; }

    public List<Dictionary<string, string?>> Rows(string table) => Tables.GetValueOrDefault(table) ?? [];

    /// <summary>Nombre de lignes par table, pour l'aperçu.</summary>
    public IEnumerable<(string Table, int Count)> Summary => Tables.Select(t => (t.Key, t.Value.Count)).OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Lecture d'une instance phpIPAM (versions 1.4 à 1.7).</summary>
public static class PhpIpamSource
{
    /// <summary>Tables de phpIPAM reprises par l'import.</summary>
    public static readonly string[] Tables =
    [
        "sections", "subnets", "ipaddresses", "vlans", "vlanDomains", "vrf", "nameservers", "devices", "deviceTypes", "locations", "racks",
        "customers", "circuitProviders", "circuits", "circuitTypes", "nat", "routing_bgp", "pstnPrefixes", "pstnNumbers", "ipTags",
        "userGroups", "users", "usersAuthMethod", "settings", "settingsMail", "instructions",
    ];

    /// <summary>Lecture directe de la base MySQL / MariaDB de phpIPAM (accès en lecture seule suffisant).</summary>
    public static async Task<PhpIpamData> ReadDatabaseAsync(string connectionString, CancellationToken cancellationToken)
    {
        PhpIpamData data = new() { FromDatabase = true };
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        HashSet<string> existing = new(StringComparer.OrdinalIgnoreCase);
        await using (MySqlCommand command = new("SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE()", connection))
        await using (DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                existing.Add(reader.GetString(0));
            }
        }
        if (!existing.Contains("ipaddresses") || !existing.Contains("subnets"))
        {
            throw new InvalidOperationException("Cette base ne ressemble pas à une base phpIPAM (tables « subnets » et « ipaddresses » absentes).");
        }

        foreach (string table in Tables.Where(existing.Contains))
        {
            List<Dictionary<string, string?>> rows = [];
            // Nom de table issu de la liste ci-dessus, jamais de la saisie : pas d'injection possible.
            await using MySqlCommand command = new($"SELECT * FROM `{table}`", connection);
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                Dictionary<string, string?> row = new(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = ToText(reader.IsDBNull(i) ? null : reader.GetValue(i));
                }
                rows.Add(row);
            }
            data.Tables[table] = rows;
        }

        await using (MySqlCommand command = new(
            "SELECT TABLE_NAME, COLUMN_NAME, COLUMN_TYPE, COLUMN_COMMENT FROM information_schema.COLUMNS " +
            "WHERE TABLE_SCHEMA = DATABASE() AND COLUMN_NAME LIKE 'custom\\_%' ORDER BY TABLE_NAME, ORDINAL_POSITION", connection))
        await using (DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                data.CustomFields.Add(new PhpIpamCustomField(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.IsDBNull(3) || reader.GetString(3).Length == 0 ? null : reader.GetString(3)));
            }
        }
        return data;
    }

    private static string? ToText(object? value) => value switch
    {
        null => null,
        DateTime date => date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        bool flag => flag ? "1" : "0",
        byte[] bytes => Encoding.UTF8.GetString(bytes),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// Lecture par l'API REST de phpIPAM (https://serveur/api/{application}/). Authentification par utilisateur et mot de passe
    /// (sécurité « user » de l'application) ou par le code de l'application (sécurité « ssl_token » / « none »).
    /// </summary>
    public static async Task<PhpIpamData> ReadApiAsync(string serverUrl, string application, string? userName, string? password, string? appCode,
        bool ignoreCertificateErrors, CancellationToken cancellationToken)
    {
        PhpIpamData data = new() { FromDatabase = false };
        using HttpClientHandler handler = new();
        if (ignoreCertificateErrors)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }
        using HttpClient http = new(handler)
        {
            BaseAddress = new Uri($"{serverUrl.TrimEnd('/')}/api/{Uri.EscapeDataString(application.Trim())}/"),
            Timeout = TimeSpan.FromMinutes(2),
        };

        string? token = appCode;
        if (!string.IsNullOrEmpty(userName))
        {
            using HttpRequestMessage login = new(HttpMethod.Post, "user/");
            login.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userName}:{password}")));
            using HttpResponseMessage response = await http.SendAsync(login, cancellationToken);
            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!response.IsSuccessStatusCode || !document.RootElement.TryGetProperty("data", out JsonElement session)
                || !session.TryGetProperty("token", out JsonElement value))
            {
                throw new InvalidOperationException($"Authentification refusée par l'API phpIPAM : {Message(document.RootElement) ?? response.ReasonPhrase}");
            }
            token = value.GetString();
        }
        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException("Renseignez un utilisateur et son mot de passe, ou le code de l'application.");
        }
        http.DefaultRequestHeaders.Add("token", token);

        List<Dictionary<string, string?>> sections = await GetAsync(http, "sections/", data, required: true) ?? [];
        data.Tables["sections"] = sections;
        List<Dictionary<string, string?>> subnets = [];
        foreach (Dictionary<string, string?> section in sections)
        {
            subnets.AddRange(await GetAsync(http, $"sections/{section["id"]}/subnets/", data) ?? []);
        }
        data.Tables["subnets"] = subnets;
        List<Dictionary<string, string?>> addresses = [];
        foreach (Dictionary<string, string?> subnet in subnets.Where(s => s.GetValueOrDefault("isFolder") != "1"))
        {
            addresses.AddRange(await GetAsync(http, $"subnets/{subnet["id"]}/addresses/", data) ?? []);
        }
        // Noms de l'API → noms des colonnes de la base.
        Rename(addresses, "ip", "ip_addr");
        Rename(addresses, "tag", "state");
        Rename(addresses, "deviceId", "switch");
        data.Tables["ipaddresses"] = addresses;

        (string Table, string[] Paths)[] others =
        [
            ("vlans", ["vlan/"]), ("vlanDomains", ["l2domains/"]), ("vrf", ["vrf/"]), ("ipTags", ["addresses/tags/", "tools/tags/"]),
            ("devices", ["devices/", "tools/devices/"]), ("deviceTypes", ["tools/device_types/", "tools/deviceTypes/"]),
            ("locations", ["tools/locations/"]), ("racks", ["tools/racks/"]), ("nameservers", ["tools/nameservers/"]),
            ("customers", ["tools/customers/"]), ("circuits", ["circuits/"]), ("circuitProviders", ["circuits/providers/"]),
            ("nat", ["tools/nat/"]),
        ];
        foreach ((string table, string[] paths) in others)
        {
            List<Dictionary<string, string?>>? rows = null;
            foreach (string path in paths)
            {
                // Un point d'accès inconnu peut aussi répondre « aucun objet » : on essaie le suivant tant que c'est vide.
                List<Dictionary<string, string?>>? found = await GetAsync(http, path, data, quiet: path != paths[^1]);
                rows = found is { Count: 0 } && rows is { Count: > 0 } ? rows : found ?? rows;
                if (rows is { Count: > 0 })
                {
                    break;
                }
            }
            data.Tables[table] = rows ?? [];
        }
        Rename(data.Rows("devices"), "ip", "ip_addr");

        // Les champs personnalisés arrivent dans les objets (« custom_* ») ; leur type est demandé à l'API quand elle le donne.
        (string Table, string Controller)[] customTables = [("subnets", "subnets"), ("ipaddresses", "addresses"), ("vlans", "vlan"), ("vrf", "vrf"), ("devices", "devices")];
        foreach ((string table, string controller) in customTables)
        {
            Dictionary<string, string> types = await CustomFieldTypesAsync(http, controller, cancellationToken);
            foreach (string column in data.Rows(table).SelectMany(r => r.Keys).Where(k => k.StartsWith("custom_", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                data.CustomFields.Add(new PhpIpamCustomField(table, column, types.GetValueOrDefault(column) ?? "varchar(255)", null));
            }
        }
        data.Warnings.Add("Source API : les utilisateurs, groupes, permissions, méthodes d'authentification et paramètres ne sont pas exposés par l'API phpIPAM et ne sont pas importés (utilisez la base de données pour un import complet).");
        return data;
    }

    private static void Rename(List<Dictionary<string, string?>> rows, string from, string to)
    {
        foreach (Dictionary<string, string?> row in rows.Where(r => r.ContainsKey(from) && !r.ContainsKey(to)))
        {
            row[to] = row[from];
        }
    }

    /// <summary>
    /// GET d'une collection : null si le point d'accès n'existe pas ou échoue (avertissement), liste vide si phpIPAM
    /// répond « aucun objet » (404).
    /// </summary>
    private static async Task<List<Dictionary<string, string?>>?> GetAsync(HttpClient http, string path, PhpIpamData data, bool required = false, bool quiet = false)
    {
        using HttpResponseMessage response = await http.GetAsync(path);
        string body = await response.Content.ReadAsStringAsync();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            if (required)
            {
                throw new InvalidOperationException($"Réponse non JSON de l'API pour « {path} » ({(int)response.StatusCode}) : vérifiez l'adresse et l'application.");
            }
            if (!quiet)
            {
                data.Warnings.Add($"API : « {path} » indisponible ({(int)response.StatusCode}), non importé.");
            }
            return null;
        }
        using (document)
        {
            JsonElement root = document.RootElement;
            bool success = root.TryGetProperty("success", out JsonElement flag) && (flag.ValueKind == JsonValueKind.True || flag.ToString() == "1");
            if (!success)
            {
                string? message = Message(root);
                // phpIPAM répond 404 « No … found » pour une collection vide.
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound && message is not null && message.Contains("found", StringComparison.OrdinalIgnoreCase))
                {
                    return [];
                }
                if (required)
                {
                    throw new InvalidOperationException($"API phpIPAM, « {path} » : {message ?? response.ReasonPhrase}");
                }
                if (!quiet)
                {
                    data.Warnings.Add($"API : « {path} » refusé ({message ?? response.ReasonPhrase}), non importé.");
                }
                return null;
            }
            if (!root.TryGetProperty("data", out JsonElement items))
            {
                return [];
            }
            IEnumerable<JsonElement> elements = items.ValueKind switch
            {
                JsonValueKind.Array => items.EnumerateArray(),
                // Certaines collections sont renvoyées en objet indexé par identifiant.
                JsonValueKind.Object when items.EnumerateObject().All(p => p.Value.ValueKind == JsonValueKind.Object) => items.EnumerateObject().Select(p => p.Value),
                JsonValueKind.Object => [items],
                _ => [],
            };
            return elements.Where(e => e.ValueKind == JsonValueKind.Object).Select(ToRow).ToList();
        }
    }

    private static Dictionary<string, string?> ToRow(JsonElement element)
    {
        Dictionary<string, string?> row = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            row[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.True => "1",
                JsonValueKind.False => "0",
                _ => property.Value.GetRawText(),
            };
        }
        return row;
    }

    private static async Task<Dictionary<string, string>> CustomFieldTypesAsync(HttpClient http, string controller, CancellationToken cancellationToken)
    {
        Dictionary<string, string> types = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            using HttpResponseMessage response = await http.GetAsync($"{controller}/custom_fields/", cancellationToken);
            using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (document.RootElement.TryGetProperty("data", out JsonElement fields) && fields.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty field in fields.EnumerateObject())
                {
                    if (field.Value.ValueKind == JsonValueKind.Object && field.Value.TryGetProperty("type", out JsonElement type))
                    {
                        types[field.Name] = type.ToString();
                    }
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or HttpRequestException)
        {
            // Types inconnus : texte par défaut.
        }
        return types;
    }

    private static string? Message(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("message", out JsonElement message) ? message.ToString() : null;
}
