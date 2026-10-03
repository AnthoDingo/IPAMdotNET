using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using IPAMdotNet.Data;
using IPAMdotNet.Networking;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Maintenance;

public sealed record CsvFormat(string Key, string Label, string[] Columns);

/// <summary>Résultat de la préparation d'un import : objets prêts à insérer, ou erreurs (l'import est tout ou rien).</summary>
public sealed record CsvImportResult(List<object> Entities, List<string> Errors, int RowCount);

/// <summary>
/// Import / export CSV. Les en-têtes d'export sont ceux attendus à l'import (aller-retour possible).
/// Les références (section, VLAN, VRF, type, emplacement) se font par nom ou numéro et doivent exister.
/// </summary>
public static class CsvTransfer
{
    public static readonly IReadOnlyList<CsvFormat> Formats =
    [
        new("vlans", "VLAN", ["numero", "nom", "description"]),
        new("vrfs", "VRF", ["nom", "rd", "description"]),
        new("sous-reseaux", "Sous-réseaux", ["section", "sous_reseau", "description", "vlan", "vrf"]),
        new("equipements", "Équipements", ["nom", "ip", "type", "emplacement", "description"]),
        new("emplacements", "Emplacements", ["nom", "adresse", "latitude", "longitude", "description"]),
        new("clients", "Clients", ["nom", "adresse", "code_postal", "ville", "contact", "telephone", "email", "notes"]),
    ];

    public static async Task<string> ExportAsync(AppDbContext db, CsvFormat format)
    {
        List<string?[]> rows = [format.Columns];
        switch (format.Key)
        {
            case "vlans":
                rows.AddRange((await db.Vlans.OrderBy(v => v.Number).ToListAsync())
                    .Select(v => new[] { v.Number.ToString(CultureInfo.InvariantCulture), v.Name, v.Description }));
                break;
            case "vrfs":
                rows.AddRange((await db.Vrfs.OrderBy(v => v.Name).ToListAsync())
                    .Select(v => new[] { v.Name, v.RouteDistinguisher, v.Description }));
                break;
            case "sous-reseaux":
                rows.AddRange((await db.Subnets.Include(s => s.Section).Include(s => s.Vlan).Include(s => s.Vrf)
                        .OrderBy(s => s.SectionId).ThenBy(s => s.Address).ThenBy(s => s.PrefixLength).ToListAsync())
                    .Select(s => new[] { s.Section?.Name, s.Network.ToString(), s.Description, s.Vlan?.Number.ToString(CultureInfo.InvariantCulture), s.Vrf?.Name }));
                break;
            case "equipements":
                rows.AddRange((await db.Devices.Include(d => d.DeviceType).Include(d => d.Location).OrderBy(d => d.Hostname).ToListAsync())
                    .Select(d => new[] { d.Hostname, d.IpAddress, d.DeviceType?.Name, d.Location?.Name, d.Description }));
                break;
            case "emplacements":
                rows.AddRange((await db.Locations.OrderBy(l => l.Name).ToListAsync())
                    .Select(l => new[] { l.Name, l.Address, l.Latitude, l.Longitude, l.Description }));
                break;
            case "clients":
                rows.AddRange((await db.Customers.OrderBy(c => c.Name).ToListAsync())
                    .Select(c => new[] { c.Name, c.Address, c.PostCode, c.City, c.ContactPerson, c.ContactPhone, c.ContactMail, c.Note }));
                break;
        }
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
        string[] missing = format.Columns.Where(c => !columns.ContainsKey(c)).ToArray();
        if (missing.Length > 0)
        {
            errors.Add($"Colonnes manquantes dans l'en-tête : {string.Join(", ", missing)}. Attendu : {string.Join(";", format.Columns)}");
            return new CsvImportResult(entities, errors, rows.Count - 1);
        }

        for (int index = 1; index < rows.Count; index++)
        {
            string[] row = rows[index];
            string? Cell(string column) => columns[column] < row.Length && row[columns[column]].Trim() is { Length: > 0 } value ? value : null;
            int line = index + 1;
            List<string> rowErrors = [];
            object? entity = format.Key switch
            {
                "vlans" => await VlanAsync(db, Cell, entities, rowErrors),
                "vrfs" => await VrfAsync(db, Cell, entities, rowErrors),
                "sous-reseaux" => await SubnetAsync(db, Cell, entities, rowErrors),
                "equipements" => await DeviceAsync(db, Cell, rowErrors),
                "emplacements" => LocationFromRow(Cell, rowErrors),
                "clients" => CustomerFromRow(Cell, rowErrors),
                _ => null,
            };
            if (entity is not null)
            {
                CheckLengths(entity, rowErrors);
            }
            if (rowErrors.Count == 0 && entity is not null)
            {
                entities.Add(entity);
            }
            errors.AddRange(rowErrors.Select(e => $"Ligne {line} : {e}"));
        }
        return new CsvImportResult(entities, errors, rows.Count - 1);
    }

    private static async Task<Vlan?> VlanAsync(AppDbContext db, Func<string, string?> cell, List<object> pending, List<string> errors)
    {
        if (!int.TryParse(cell("numero"), out int number) || number is < 1 or > 4094)
        {
            errors.Add("numéro de VLAN entre 1 et 4094 attendu.");
            return null;
        }
        if (await db.Vlans.AnyAsync(v => v.Number == number) || pending.OfType<Vlan>().Any(v => v.Number == number))
        {
            errors.Add($"le VLAN {number} existe déjà.");
        }
        string? name = Required(cell("nom"), "nom", errors);
        return new Vlan { Number = number, Name = name ?? "", Description = cell("description") };
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
            int? number = int.TryParse(vlanText, out int parsed) ? parsed : null;
            Vlan? vlan = number is null ? null : await db.Vlans.SingleOrDefaultAsync(v => v.Number == number);
            if (vlan is null)
            {
                errors.Add($"VLAN « {vlanText} » inconnu.");
            }
            subnet.VlanId = vlan?.Id;
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
