using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using IPAMdotNet.Data;
using IPAMdotNet.Networking;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Maintenance;

/// <param name="EntityType">Type porteur des champs personnalisés (clé de <see cref="CustomField.SupportedTypes"/>).</param>
/// <summary>Format CSV. <paramref name="Optional"/> : colonnes exportées mais facultatives à l'import (fichiers antérieurs).</summary>
public sealed record CsvFormat(string Key, string Label, string EntityType, string[] Columns, string[]? Optional = null);

/// <summary>
/// Résultat de la préparation d'un import : objets prêts à insérer, ou erreurs (l'import est tout ou rien).
/// <see cref="CustomValues"/> : valeurs de champs personnalisés par objet, à enregistrer une fois l'identifiant connu.
/// </summary>
public sealed record CsvImportResult(List<object> Entities, List<string> Errors, int RowCount)
{
    public Dictionary<object, Dictionary<int, string?>> CustomValues { get; } = new(ReferenceEqualityComparer.Instance);
}

/// <summary>
/// Import / export CSV. Les en-têtes d'export sont ceux attendus à l'import (aller-retour possible).
/// Les références (section, VLAN, VRF, type, emplacement) se font par nom ou numéro et doivent exister.
/// </summary>
public static class CsvTransfer
{
    public static readonly IReadOnlyList<CsvFormat> Formats =
    [
        new("vlans", "VLAN", nameof(Vlan), ["numero", "nom", "description", "domaine"], ["domaine"]),
        new("vrfs", "VRF", nameof(Vrf), ["nom", "rd", "description"]),
        new("sous-reseaux", "Sous-réseaux", nameof(Subnet), ["section", "sous_reseau", "description", "vlan", "vrf", "domaine_vlan"], ["domaine_vlan"]),
        new("adresses", "Adresses IP", nameof(IpAddress), ["section", "sous_reseau", "adresse", "nom_hote", "description", "mac", "proprietaire", "etiquette"]),
        new("equipements", "Équipements", nameof(Device), ["nom", "ip", "type", "emplacement", "description"]),
        new("emplacements", "Emplacements", nameof(Location), ["nom", "adresse", "latitude", "longitude", "description"]),
        new("clients", "Clients", nameof(Customer), ["nom", "adresse", "code_postal", "ville", "contact", "telephone", "email", "notes"]),
    ];

    /// <summary>Export : colonnes du format, puis une colonne par champ personnalisé (valeurs sous leur forme invariante).</summary>
    public static async Task<string> ExportAsync(AppDbContext db, CsvFormat format)
    {
        List<(int Id, string?[] Cells)> data = format.Key switch
        {
            "vlans" => (await db.Vlans.Include(v => v.Domain).OrderBy(v => v.Number).ThenBy(v => v.Domain!.Name).ToListAsync())
                .Select(v => (v.Id, new[] { v.Number.ToString(CultureInfo.InvariantCulture), v.Name, v.Description, v.Domain?.Name })).ToList(),
            "vrfs" => (await db.Vrfs.OrderBy(v => v.Name).ToListAsync())
                .Select(v => (v.Id, new[] { v.Name, v.RouteDistinguisher, v.Description })).ToList(),
            "sous-reseaux" => (await db.Subnets.Include(s => s.Section).Include(s => s.Vlan).ThenInclude(v => v!.Domain).Include(s => s.Vrf)
                    .OrderBy(s => s.SectionId).ThenBy(s => s.Address).ThenBy(s => s.PrefixLength).ToListAsync())
                .Select(s => (s.Id, new[] { s.Section?.Name, s.Network.ToString(), s.Description, s.Vlan?.Number.ToString(CultureInfo.InvariantCulture), s.Vrf?.Name, s.Vlan?.Domain?.Name })).ToList(),
            "adresses" => (await db.IpAddresses.Include(a => a.Subnet).ThenInclude(s => s!.Section).Include(a => a.Tag)
                    .OrderBy(a => a.Subnet!.SectionId).ThenBy(a => a.Address).ToListAsync())
                .Select(a => (a.Id, new[] { a.Subnet?.Section?.Name, a.Subnet?.Network.ToString(), a.Value.ToString(), a.Hostname, a.Description, a.MacAddress, a.Owner, a.Tag?.Name })).ToList(),
            "equipements" => (await db.Devices.Include(d => d.DeviceType).Include(d => d.Location).OrderBy(d => d.Hostname).ToListAsync())
                .Select(d => (d.Id, new[] { d.Hostname, d.IpAddress, d.DeviceType?.Name, d.Location?.Name, d.Description })).ToList(),
            "emplacements" => (await db.Locations.OrderBy(l => l.Name).ToListAsync())
                .Select(l => (l.Id, new[] { l.Name, l.Address, l.Latitude, l.Longitude, l.Description })).ToList(),
            "clients" => (await db.Customers.OrderBy(c => c.Name).ToListAsync())
                .Select(c => (c.Id, new[] { c.Name, c.Address, c.PostCode, c.City, c.ContactPerson, c.ContactPhone, c.ContactMail, c.Note })).ToList(),
            _ => [],
        };
        List<CustomField> fields = await CustomFieldForm.DefinitionsAsync(db, format.EntityType);
        Dictionary<int, Dictionary<int, string>> values = fields.Count == 0 ? [] : await CustomFieldForm.ValuesForAsync(db, format.EntityType);
        List<string?[]> rows = [[.. format.Columns, .. fields.Select(f => f.Name)]];
        rows.AddRange(data.Select(row => (string?[])[.. row.Cells, .. fields.Select(f => values.GetValueOrDefault(row.Id)?.GetValueOrDefault(f.Id))]));
        return Csv.Write(rows);
    }

    public static async Task<CsvImportResult> PrepareAsync(AppDbContext db, CsvFormat format, List<string[]> rows)
    {
        List<object> entities = [];
        List<string> errors = [];
        if (rows.Count == 0)
        {
            errors.Add("Le fichier est vide.");
            return new CsvImportResult(entities, errors, 0);
        }

        // En-têtes comparés sans casse ni accents ; l'ordre des colonnes est libre, les colonnes inconnues sont ignorées.
        Dictionary<string, int> columns = rows[0].Select((name, index) => (Name: Key(name), Index: index))
            .GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First().Index);
        string[] missing = format.Columns.Where(c => !columns.ContainsKey(c) && format.Optional?.Contains(c) != true).ToArray();
        if (missing.Length > 0)
        {
            errors.Add($"Colonnes manquantes dans l'en-tête : {string.Join(", ", missing)}. Attendu : {string.Join(";", format.Columns)}");
            return new CsvImportResult(entities, errors, rows.Count - 1);
        }

        // Champs personnalisés : colonne facultative portant le nom du champ (un champ obligatoire absent est une erreur par ligne).
        List<CustomField> fields = (await CustomFieldForm.DefinitionsAsync(db, format.EntityType))
            .Where(f => !format.Columns.Contains(Key(f.Name))).ToList();
        CsvImportResult result = new(entities, errors, rows.Count - 1);

        for (int index = 1; index < rows.Count; index++)
        {
            string[] row = rows[index];
            string? Cell(string column) => columns.TryGetValue(column, out int at) && at < row.Length && row[at].Trim() is { Length: > 0 } value ? value : null;
            int line = index + 1;
            List<string> rowErrors = [];
            object? entity = format.Key switch
            {
                "vlans" => await VlanAsync(db, Cell, entities, rowErrors),
                "vrfs" => await VrfAsync(db, Cell, entities, rowErrors),
                "sous-reseaux" => await SubnetAsync(db, Cell, entities, rowErrors),
                "adresses" => await AddressAsync(db, Cell, entities, rowErrors),
                "equipements" => await DeviceAsync(db, Cell, rowErrors),
                "emplacements" => LocationFromRow(Cell, rowErrors),
                "clients" => CustomerFromRow(Cell, rowErrors),
                _ => null,
            };
            if (entity is not null)
            {
                CheckLengths(entity, rowErrors);
            }
            Dictionary<int, string?> posted = fields.Where(f => columns.ContainsKey(Key(f.Name)))
                .ToDictionary(f => f.Id, f => Cell(Key(f.Name)));
            ModelStateDictionary state = new();
            Dictionary<int, string?> custom = CustomFieldForm.Validate(fields, posted, state);
            rowErrors.AddRange(state.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            if (rowErrors.Count == 0 && entity is not null)
            {
                entities.Add(entity);
                if (custom.Count > 0)
                {
                    result.CustomValues[entity] = custom;
                }
            }
            errors.AddRange(rowErrors.Select(e => $"Ligne {line} : {e}"));
        }
        return result;
    }

    private static async Task<Vlan?> VlanAsync(AppDbContext db, Func<string, string?> cell, List<object> pending, List<string> errors)
    {
        if (!int.TryParse(cell("numero"), out int number) || number is < 1 or > 4094)
        {
            errors.Add("numéro de VLAN entre 1 et 4094 attendu.");
            return null;
        }
        // Domaine L2 par nom ; vide = domaine par défaut.
        string? domainName = cell("domaine");
        VlanDomain? domain = domainName is null ? await db.VlanDomains.OrderBy(d => d.Id).FirstAsync() : await db.VlanDomains.SingleOrDefaultAsync(d => d.Name == domainName);
        if (domain is null)
        {
            errors.Add($"domaine L2 « {domainName} » inconnu.");
            return null;
        }
        if (await db.Vlans.AnyAsync(v => v.DomainId == domain.Id && v.Number == number) || pending.OfType<Vlan>().Any(v => v.DomainId == domain.Id && v.Number == number))
        {
            errors.Add($"le VLAN {number} existe déjà dans le domaine « {domain.Name} ».");
        }
        string? name = Required(cell("nom"), "nom", errors);
        return new Vlan { DomainId = domain.Id, Number = number, Name = name ?? "", Description = cell("description") };
    }

    private static async Task<Vrf?> VrfAsync(AppDbContext db, Func<string, string?> cell, List<object> pending, List<string> errors)
    {
        string? name = Required(cell("nom"), "nom", errors);
        if (name is not null && (await db.Vrfs.AnyAsync(v => v.Name == name) || pending.OfType<Vrf>().Any(v => v.Name == name)))
        {
            errors.Add($"la VRF « {name} » existe déjà.");
        }
        return new Vrf { Name = name ?? "", RouteDistinguisher = cell("rd"), Description = cell("description") };
    }

    private static async Task<Subnet?> SubnetAsync(AppDbContext db, Func<string, string?> cell, List<object> pending, List<string> errors)
    {
        string? sectionName = Required(cell("section"), "section", errors);
        Section? section = sectionName is null ? null : await db.Sections.SingleOrDefaultAsync(s => s.Name == sectionName);
        if (sectionName is not null && section is null)
        {
            errors.Add($"section « {sectionName} » inconnue.");
        }
        Subnet subnet = new() { Description = cell("description") };
        string? cidr = Required(cell("sous_reseau"), "sous_reseau", errors);
        if (cidr is not null)
        {
            if (!Ip.TryParseNetwork(cidr, out IPNetwork network))
            {
                errors.Add($"« {cidr} » n'est pas un réseau valide (bits d'hôte à zéro).");
            }
            else
            {
                subnet.SetNetwork(network);
            }
        }
        if (section is not null && subnet.Address.Length == 16)
        {
            subnet.SectionId = section.Id;
            if (await db.Subnets.AnyAsync(s => s.SectionId == section.Id && s.Address == subnet.Address && s.PrefixLength == subnet.PrefixLength)
                || pending.OfType<Subnet>().Any(s => s.SectionId == section.Id && s.Address.SequenceEqual(subnet.Address) && s.PrefixLength == subnet.PrefixLength))
            {
                errors.Add($"{subnet.Network} existe déjà dans la section « {section.Name} ».");
            }
        }
        if (cell("vlan") is { } vlanText)
        {
            // Un même numéro peut exister dans plusieurs domaines L2 : « domaine_vlan » lève l'ambiguïté.
            // Seuls les VLAN des domaines ouverts à la section sont candidats (comme le formulaire).
            int? number = int.TryParse(vlanText, out int parsed) ? parsed : null;
            string? domainName = cell("domaine_vlan");
            IQueryable<Vlan> candidates = section is null ? db.Vlans : Vlan.AvailableIn(db.Vlans, section.Id);
            List<Vlan> matches = number is null ? [] : await candidates.Where(v => v.Number == number && (domainName == null || v.Domain!.Name == domainName)).Take(2).ToListAsync();
            if (matches.Count == 0)
            {
                errors.Add($"VLAN « {vlanText} »{(domainName is null ? "" : $" du domaine « {domainName} »")} inconnu ou non proposé dans cette section.");
            }
            else if (matches.Count > 1)
            {
                errors.Add($"VLAN {number} présent dans plusieurs domaines L2 : précisez la colonne « domaine_vlan ».");
            }
            subnet.VlanId = matches.Count == 1 ? matches[0].Id : null;
        }
        if (cell("vrf") is { } vrfName)
        {
            Vrf? vrf = await db.Vrfs.SingleOrDefaultAsync(v => v.Name == vrfName);
            if (vrf is null)
            {
                errors.Add($"VRF « {vrfName} » inconnue.");
            }
            subnet.VrfId = vrf?.Id;
        }
        return subnet;
    }

    /// <summary>Adresse rattachée au sous-réseau désigné par sa section et son CIDR (qui doivent exister).</summary>
    private static async Task<IpAddress?> AddressAsync(AppDbContext db, Func<string, string?> cell, List<object> pending, List<string> errors)
    {
        string? sectionName = Required(cell("section"), "section", errors);
        string? cidr = Required(cell("sous_reseau"), "sous_reseau", errors);
        string? text = Required(cell("adresse"), "adresse", errors);
        if (sectionName is null || cidr is null || text is null)
        {
            return null;
        }
        if (!Ip.TryParseNetwork(cidr, out IPNetwork network))
        {
            errors.Add($"« {cidr} » n'est pas un réseau valide.");
            return null;
        }
        byte[] subnetBytes = Ip.ToBytes(network.BaseAddress);
        Subnet? subnet = await db.Subnets.SingleOrDefaultAsync(s => s.Section!.Name == sectionName && s.Address == subnetBytes && s.PrefixLength == network.PrefixLength);
        if (subnet is null)
        {
            errors.Add($"sous-réseau {network} introuvable dans la section « {sectionName} ».");
            return null;
        }
        if (!IPAddress.TryParse(text, out IPAddress? address) || !Ip.Contains(network, new IPNetwork(address, address.GetAddressBytes().Length * 8)))
        {
            errors.Add($"« {text} » : adresse invalide ou hors de {network}.");
            return null;
        }
        (System.Numerics.BigInteger first, System.Numerics.BigInteger last) = Ip.UsableRange(network);
        System.Numerics.BigInteger value = Ip.ToNumber(address);
        if (subnet.IsIPv4 && (value < first || value > last))
        {
            errors.Add($"{address} : adresse réseau ou de diffusion, non attribuable.");
        }
        IpAddress entry = new()
        {
            SubnetId = subnet.Id,
            Address = Ip.ToBytes(address),
            Hostname = cell("nom_hote"),
            Description = cell("description"),
            Owner = cell("proprietaire"),
        };
        if (await db.IpAddresses.AnyAsync(a => a.SubnetId == subnet.Id && a.Address == entry.Address)
            || pending.OfType<IpAddress>().Any(a => a.SubnetId == subnet.Id && a.Address.SequenceEqual(entry.Address)))
        {
            errors.Add($"{address} existe déjà dans {network}.");
        }
        if (cell("mac") is { } mac)
        {
            entry.MacAddress = IpAddress.NormalizeMac(mac);
            if (entry.MacAddress is null)
            {
                errors.Add($"adresse MAC « {mac} » invalide.");
            }
        }
        if (cell("etiquette") is { } tagName)
        {
            Tag? tag = await db.Tags.SingleOrDefaultAsync(t => t.Name == tagName);
            if (tag is null)
            {
                errors.Add($"étiquette « {tagName} » inconnue.");
            }
            entry.TagId = tag?.Id;
        }
        return entry;
    }

    private static async Task<Device?> DeviceAsync(AppDbContext db, Func<string, string?> cell, List<string> errors)
    {
        Device device = new() { Hostname = Required(cell("nom"), "nom", errors) ?? "", Description = cell("description") };
        if (cell("ip") is { } ip)
        {
            if (IPAddress.TryParse(ip, out IPAddress? address))
            {
                device.IpAddress = address.ToString();
            }
            else
            {
                errors.Add($"adresse IP « {ip} » invalide.");
            }
        }
        if (cell("type") is { } typeName)
        {
            DeviceType? type = await db.DeviceTypes.SingleOrDefaultAsync(t => t.Name == typeName);
            if (type is null)
            {
                errors.Add($"type « {typeName} » inconnu.");
            }
            device.DeviceTypeId = type?.Id;
        }
        if (cell("emplacement") is { } locationName)
        {
            Location? location = await db.Locations.FirstOrDefaultAsync(l => l.Name == locationName);
            if (location is null)
            {
                errors.Add($"emplacement « {locationName} » inconnu.");
            }
            device.LocationId = location?.Id;
        }
        return device;
    }

    private static Location LocationFromRow(Func<string, string?> cell, List<string> errors)
    {
        Location location = new() { Name = Required(cell("nom"), "nom", errors) ?? "", Address = cell("adresse"), Description = cell("description") };
        if (Location.TryNormalizeCoordinate(cell("latitude"), 90, out string? latitude))
        {
            location.Latitude = latitude;
        }
        else
        {
            errors.Add("latitude invalide (nombre entre -90 et 90).");
        }
        if (Location.TryNormalizeCoordinate(cell("longitude"), 180, out string? longitude))
        {
            location.Longitude = longitude;
        }
        else
        {
            errors.Add("longitude invalide (nombre entre -180 et 180).");
        }
        return location;
    }

    private static Customer CustomerFromRow(Func<string, string?> cell, List<string> errors)
    {
        Customer customer = new()
        {
            Name = Required(cell("nom"), "nom", errors) ?? "",
            Address = cell("adresse"),
            PostCode = cell("code_postal"),
            City = cell("ville"),
            ContactPerson = cell("contact"),
            ContactPhone = cell("telephone"),
            ContactMail = cell("email"),
            Note = cell("notes"),
        };
        if (customer.ContactMail is not null && !new EmailAddressAttribute().IsValid(customer.ContactMail))
        {
            errors.Add($"e-mail « {customer.ContactMail} » invalide.");
        }
        return customer;
    }

    private static string? Required(string? value, string column, List<string> errors)
    {
        if (value is null)
        {
            errors.Add($"la colonne « {column} » est obligatoire.");
        }
        return value;
    }

    /// <summary>Longueurs maximales des colonnes texte (attributs [MaxLength] de l'entité), messages en français.</summary>
    private static void CheckLengths(object entity, List<string> errors)
    {
        foreach (PropertyInfo property in entity.GetType().GetProperties().Where(p => p.PropertyType == typeof(string)))
        {
            MaxLengthAttribute? maxLength = property.GetCustomAttribute<MaxLengthAttribute>();
            if (maxLength is not null && property.GetValue(entity) is string value && value.Length > maxLength.Length)
            {
                string label = property.GetCustomAttribute<DisplayAttribute>()?.Name ?? property.Name;
                errors.Add($"« {label} » dépasse {maxLength.Length} caractères.");
            }
        }
    }

    /// <summary>« Code postal » → « code_postal », « Numéro » → « numero ».</summary>
    private static string Key(string header)
    {
        string decomposed = header.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        StringBuilder builder = new();
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c is ' ' or '-' ? '_' : c);
            }
        }
        return builder.ToString();
    }
}
