using IPAMdotNet.Localization;
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
/// Résultat de la préparation d'un import : objets prêts à insérer (<see cref="Entities"/>) ou objets existants déjà modifiés
/// dans le contexte (<see cref="Updated"/>, enregistrés par SaveChanges), ou erreurs (l'import est tout ou rien).
/// <see cref="CustomValues"/> : valeurs de champs personnalisés par objet, à enregistrer une fois l'identifiant connu.
/// </summary>
public sealed record CsvImportResult(List<object> Entities, List<string> Errors, int RowCount)
{
    public List<object> Updated { get; } = [];

    public Dictionary<object, Dictionary<int, string?>> CustomValues { get; } = new(ReferenceEqualityComparer.Instance);
}

/// <summary>
/// Import / export CSV (export aussi en Excel). Les en-têtes d'export sont ceux attendus à l'import (aller-retour possible).
/// Les références (section, VLAN, VRF, type, emplacement) se font par nom ou numéro et doivent exister.
/// Un objet est reconnu par sa clé naturelle (<see cref="ExistingAsync"/>) : en mise à jour, ses colonnes sont remplacées
/// (cellule vide = valeur effacée), sinon il est refusé — sauf équipements, emplacements et clients, dont le nom n'est pas unique
/// et qui sont alors toujours créés.
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
        new("clients", "Clients", nameof(Customer), ["nom", "adresse", "code_postal", "ville", "latitude", "longitude", "contact", "telephone", "email", "notes"],
            ["latitude", "longitude"]),
    ];

    /// <summary>Propriétés remplacées lors d'une mise à jour (les autres colonnes forment la clé de l'objet).</summary>
    private static readonly Dictionary<Type, string[]> Updatable = new()
    {
        [typeof(Vlan)] = [nameof(Vlan.Name), nameof(Vlan.Description)],
        [typeof(Vrf)] = [nameof(Vrf.RouteDistinguisher), nameof(Vrf.Description)],
        [typeof(Subnet)] = [nameof(Subnet.Description), nameof(Subnet.VlanId), nameof(Subnet.VrfId)],
        [typeof(IpAddress)] = [nameof(IpAddress.Hostname), nameof(IpAddress.Description), nameof(IpAddress.MacAddress), nameof(IpAddress.Owner), nameof(IpAddress.TagId)],
        [typeof(Device)] = [nameof(Device.IpAddress), nameof(Device.DeviceTypeId), nameof(Device.LocationId), nameof(Device.Description)],
        [typeof(Location)] = [nameof(Location.Address), nameof(Location.Latitude), nameof(Location.Longitude), nameof(Location.Description)],
        [typeof(Customer)] = [nameof(Customer.Address), nameof(Customer.PostCode), nameof(Customer.City), nameof(Customer.Latitude), nameof(Customer.Longitude), nameof(Customer.ContactPerson),
            nameof(Customer.ContactPhone), nameof(Customer.ContactMail), nameof(Customer.Note)],
    };

    /// <summary>Export : en-tête (colonnes du format, puis une par champ personnalisé) et lignes, valeurs sous leur forme invariante.</summary>
    public static async Task<List<string?[]>> ExportAsync(AppDbContext db, CsvFormat format)
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
                .Select(c => (c.Id, new[] { c.Name, c.Address, c.PostCode, c.City, c.Latitude, c.Longitude, c.ContactPerson, c.ContactPhone, c.ContactMail, c.Note })).ToList(),
            _ => [],
        };
        List<CustomField> fields = await CustomFieldForm.DefinitionsAsync(db, format.EntityType);
        Dictionary<int, Dictionary<int, string>> values = fields.Count == 0 ? [] : await CustomFieldForm.ValuesForAsync(db, format.EntityType);
        List<string?[]> rows = [[.. format.Columns, .. fields.Select(f => f.Name)]];
        rows.AddRange(data.Select(row => (string?[])[.. row.Cells, .. fields.Select(f => values.GetValueOrDefault(row.Id)?.GetValueOrDefault(f.Id))]));
        return rows;
    }

    /// <param name="update">Objets existants mis à jour ; sinon refusés (formats à clé unique) ou recréés.</param>
    public static async Task<CsvImportResult> PrepareAsync(AppDbContext db, CsvFormat format, List<string[]> rows, bool update)
    {
        List<object> entities = [];
        List<string> errors = [];
        if (rows.Count == 0)
        {
            errors.Add(L.T("Le fichier est vide."));
            return new CsvImportResult(entities, errors, 0);
        }

        // En-têtes comparés sans casse ni accents ; l'ordre des colonnes est libre, les colonnes inconnues sont ignorées.
        Dictionary<string, int> columns = rows[0].Select((name, index) => (Name: Key(name), Index: index))
            .GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First().Index);
        string[] missing = format.Columns.Where(c => !columns.ContainsKey(c) && format.Optional?.Contains(c) != true).ToArray();
        if (missing.Length > 0)
        {
            errors.Add(L.T("Colonnes manquantes dans l'en-tête : {0}. Attendu : {1}", string.Join(", ", missing), string.Join(";", format.Columns)));
            return new CsvImportResult(entities, errors, rows.Count - 1);
        }

        // Champs personnalisés : colonne facultative portant le nom du champ (un champ obligatoire absent est une erreur par ligne).
        List<CustomField> fields = (await CustomFieldForm.DefinitionsAsync(db, format.EntityType))
            .Where(f => !format.Columns.Contains(Key(f.Name))).ToList();
        CsvImportResult result = new(entities, errors, rows.Count - 1);
        // Clés vues dans le fichier : une même ligne deux fois créerait un doublon ou mettrait à jour deux fois le même objet.
        HashSet<string> keys = [];
        bool unique = format.Key is "vlans" or "vrfs" or "sous-reseaux" or "adresses";

        for (int index = 1; index < rows.Count; index++)
        {
            string[] row = rows[index];
            string? Cell(string column) => columns.TryGetValue(column, out int at) && at < row.Length && row[at].Trim() is { Length: > 0 } value ? value : null;
            int line = index + 1;
            List<string> rowErrors = [];
            object? entity = format.Key switch
            {
                "vlans" => await VlanAsync(db, Cell, rowErrors),
                "vrfs" => VrfFromRow(Cell, rowErrors),
                "sous-reseaux" => await SubnetAsync(db, Cell, rowErrors),
                "adresses" => await AddressAsync(db, Cell, rowErrors),
                "equipements" => await DeviceAsync(db, Cell, rowErrors),
                "emplacements" => LocationFromRow(Cell, rowErrors),
                "clients" => CustomerFromRow(Cell, rowErrors),
                _ => null,
            };
            if (entity is not null)
            {
                CheckLengths(entity, rowErrors);
            }
            object? existing = null;
            if (entity is not null && rowErrors.Count == 0 && (unique || update))
            {
                List<object> matches = await ExistingAsync(db, entity);
                if (!keys.Add(NaturalKey(entity)))
                {
                    rowErrors.Add(L.T("{0} figure déjà sur une ligne précédente du fichier.", Describe(entity)));
                }
                else if (matches.Count > 1)
                {
                    rowErrors.Add(L.T("plusieurs objets correspondent à {0} : mise à jour impossible.", Describe(entity)));
                }
                else if (matches.Count == 1 && !update)
                {
                    rowErrors.Add(L.T("{0} existe déjà (cochez « Mettre à jour les objets existants »).", Describe(entity)));
                }
                existing = matches.Count == 1 ? matches[0] : null;
            }
            // Mise à jour : seuls les champs personnalisés présents dans le fichier sont repris (les autres gardent leur valeur).
            Dictionary<int, string?> posted = fields.Where(f => columns.ContainsKey(Key(f.Name)))
                .ToDictionary(f => f.Id, f => Cell(Key(f.Name)));
            ModelStateDictionary state = new();
            Dictionary<int, string?> custom = CustomFieldForm.Validate(existing is null ? fields : fields.Where(f => posted.ContainsKey(f.Id)).ToList(), posted, state);
            rowErrors.AddRange(state.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            if (rowErrors.Count == 0 && entity is not null)
            {
                object target = entity;
                if (existing is not null)
                {
                    foreach (string property in Updatable[entity.GetType()])
                    {
                        db.Entry(existing).Property(property).CurrentValue = entity.GetType().GetProperty(property)!.GetValue(entity);
                    }
                    result.Updated.Add(existing);
                    target = existing;
                }
                else
                {
                    entities.Add(entity);
                }
                if (custom.Count > 0)
                {
                    result.CustomValues[target] = custom;
                }
            }
            errors.AddRange(rowErrors.Select(e => L.T("Ligne {0} : {1}", line, e)));
        }
        return result;
    }

    /// <summary>Objets existants de même clé naturelle (suivis : une mise à jour les modifie directement), deux au plus.</summary>
    private static async Task<List<object>> ExistingAsync(AppDbContext db, object entity) => entity switch
    {
        Vlan v => [.. await db.Vlans.Where(x => x.DomainId == v.DomainId && x.Number == v.Number).Take(2).ToListAsync()],
        Vrf v => [.. await db.Vrfs.Where(x => x.Name == v.Name).Take(2).ToListAsync()],
        Subnet s => [.. await db.Subnets.Where(x => x.SectionId == s.SectionId && x.Address == s.Address && x.PrefixLength == s.PrefixLength).Take(2).ToListAsync()],
        IpAddress a => [.. await db.IpAddresses.Where(x => x.SubnetId == a.SubnetId && x.Address == a.Address).Take(2).ToListAsync()],
        Device d => [.. await db.Devices.Where(x => x.Hostname == d.Hostname).Take(2).ToListAsync()],
        Location l => [.. await db.Locations.Where(x => x.Name == l.Name).Take(2).ToListAsync()],
        Customer c => [.. await db.Customers.Where(x => x.Name == c.Name).Take(2).ToListAsync()],
        _ => [],
    };

    /// <summary>Clé naturelle, pour repérer une même ligne deux fois dans le fichier (noms sans casse, comme SQL Server et MySQL).</summary>
    private static string NaturalKey(object entity) => entity switch
    {
        Vlan v => $"{v.DomainId}|{v.Number}",
        Subnet s => $"{s.SectionId}|{Convert.ToHexString(s.Address)}|{s.PrefixLength}",
        IpAddress a => $"{a.SubnetId}|{Convert.ToHexString(a.Address)}",
        Vrf v => v.Name.ToLowerInvariant(),
        Device d => d.Hostname.ToLowerInvariant(),
        Location l => l.Name.ToLowerInvariant(),
        Customer c => c.Name.ToLowerInvariant(),
        _ => "",
    };

    private static string Describe(object entity) => entity switch
    {
        Vlan v => L.T("le VLAN {0}", v.Number),
        Subnet s => L.T("le sous-réseau {0}", s.Network),
        IpAddress a => L.T("l'adresse {0}", a.Value),
        Vrf v => L.T("la VRF « {0} »", v.Name),
        Device d => L.T("l'équipement « {0} »", d.Hostname),
        Location l => L.T("l'emplacement « {0} »", l.Name),
        Customer c => L.T("le client « {0} »", c.Name),
        _ => "l'objet",
    };

    private static async Task<Vlan?> VlanAsync(AppDbContext db, Func<string, string?> cell, List<string> errors)
    {
        if (!int.TryParse(cell("numero"), out int number) || number is < 1 or > 4094)
        {
            errors.Add(L.T("numéro de VLAN entre 1 et 4094 attendu."));
            return null;
        }
        // Domaine L2 par nom ; vide = domaine par défaut.
        string? domainName = cell("domaine");
        VlanDomain? domain = domainName is null ? await db.VlanDomains.OrderBy(d => d.Id).FirstAsync() : await db.VlanDomains.SingleOrDefaultAsync(d => d.Name == domainName);
        if (domain is null)
        {
            errors.Add(L.T("domaine L2 « {0} » inconnu.", domainName));
            return null;
        }
        string? name = Required(cell("nom"), "nom", errors);
        return new Vlan { DomainId = domain.Id, Number = number, Name = name ?? "", Description = cell("description") };
    }

    private static Vrf VrfFromRow(Func<string, string?> cell, List<string> errors)
    {
        string? name = Required(cell("nom"), "nom", errors);
        return new Vrf { Name = name ?? "", RouteDistinguisher = cell("rd"), Description = cell("description") };
    }

    private static async Task<Subnet?> SubnetAsync(AppDbContext db, Func<string, string?> cell, List<string> errors)
    {
        string? sectionName = Required(cell("section"), "section", errors);
        Section? section = sectionName is null ? null : await db.Sections.SingleOrDefaultAsync(s => s.Name == sectionName);
        if (sectionName is not null && section is null)
        {
            errors.Add(L.T("section « {0} » inconnue.", sectionName));
        }
        Subnet subnet = new() { Description = cell("description") };
        string? cidr = Required(cell("sous_reseau"), "sous_reseau", errors);
        if (cidr is not null)
        {
            if (!Ip.TryParseNetwork(cidr, out IPNetwork network))
            {
                errors.Add(L.T("« {0} » n'est pas un réseau valide (bits d'hôte à zéro).", cidr));
            }
            else
            {
                subnet.SetNetwork(network);
            }
        }
        if (section is not null)
        {
            subnet.SectionId = section.Id;
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
                errors.Add(domainName is null ? L.T("VLAN « {0} » inconnu ou non proposé dans cette section.", vlanText)
                    : L.T("VLAN « {0} » du domaine « {1} » inconnu ou non proposé dans cette section.", vlanText, domainName));
            }
            else if (matches.Count > 1)
            {
                errors.Add(L.T("VLAN {0} présent dans plusieurs domaines L2 : précisez la colonne « domaine_vlan ».", number));
            }
            subnet.VlanId = matches.Count == 1 ? matches[0].Id : null;
        }
        if (cell("vrf") is { } vrfName)
        {
            Vrf? vrf = await db.Vrfs.SingleOrDefaultAsync(v => v.Name == vrfName);
            if (vrf is null)
            {
                errors.Add(L.T("VRF « {0} » inconnue.", vrfName));
            }
            subnet.VrfId = vrf?.Id;
        }
        return subnet;
    }

    /// <summary>Adresse rattachée au sous-réseau désigné par sa section et son CIDR (qui doivent exister).</summary>
    private static async Task<IpAddress?> AddressAsync(AppDbContext db, Func<string, string?> cell, List<string> errors)
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
            errors.Add(L.T("« {0} » n'est pas un réseau valide.", cidr));
            return null;
        }
        byte[] subnetBytes = Ip.ToBytes(network.BaseAddress);
        Subnet? subnet = await db.Subnets.SingleOrDefaultAsync(s => s.Section!.Name == sectionName && s.Address == subnetBytes && s.PrefixLength == network.PrefixLength);
        if (subnet is null)
        {
            errors.Add(L.T("sous-réseau {0} introuvable dans la section « {1} ».", network, sectionName));
            return null;
        }
        if (!IPAddress.TryParse(text, out IPAddress? address) || !Ip.Contains(network, new IPNetwork(address, address.GetAddressBytes().Length * 8)))
        {
            errors.Add(L.T("« {0} » : adresse invalide ou hors de {1}.", text, network));
            return null;
        }
        (System.Numerics.BigInteger first, System.Numerics.BigInteger last) = Ip.UsableRange(network);
        System.Numerics.BigInteger value = Ip.ToNumber(address);
        if (subnet.IsIPv4 && (value < first || value > last))
        {
            errors.Add(L.T("{0} : adresse réseau ou de diffusion, non attribuable.", address));
        }
        IpAddress entry = new()
        {
            SubnetId = subnet.Id,
            Address = Ip.ToBytes(address),
            Hostname = cell("nom_hote"),
            Description = cell("description"),
            Owner = cell("proprietaire"),
        };
        if (cell("mac") is { } mac)
        {
            entry.MacAddress = IpAddress.NormalizeMac(mac);
            if (entry.MacAddress is null)
            {
                errors.Add(L.T("adresse MAC « {0} » invalide.", mac));
            }
        }
        if (cell("etiquette") is { } tagName)
        {
            Tag? tag = await db.Tags.SingleOrDefaultAsync(t => t.Name == tagName);
            if (tag is null)
            {
                errors.Add(L.T("étiquette « {0} » inconnue.", tagName));
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
                errors.Add(L.T("adresse IP « {0} » invalide.", ip));
            }
        }
        if (cell("type") is { } typeName)
        {
            DeviceType? type = await db.DeviceTypes.SingleOrDefaultAsync(t => t.Name == typeName);
            if (type is null)
            {
                errors.Add(L.T("type « {0} » inconnu.", typeName));
            }
            device.DeviceTypeId = type?.Id;
        }
        if (cell("emplacement") is { } locationName)
        {
            Location? location = await db.Locations.FirstOrDefaultAsync(l => l.Name == locationName);
            if (location is null)
            {
                errors.Add(L.T("emplacement « {0} » inconnu.", locationName));
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
            errors.Add(L.T("latitude invalide (nombre entre -90 et 90)."));
        }
        if (Location.TryNormalizeCoordinate(cell("longitude"), 180, out string? longitude))
        {
            location.Longitude = longitude;
        }
        else
        {
            errors.Add(L.T("longitude invalide (nombre entre -180 et 180)."));
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
        if (Location.TryNormalizeCoordinate(cell("latitude"), 90, out string? latitude))
        {
            customer.Latitude = latitude;
        }
        else
        {
            errors.Add(L.T("latitude invalide (nombre entre -90 et 90)."));
        }
        if (Location.TryNormalizeCoordinate(cell("longitude"), 180, out string? longitude))
        {
            customer.Longitude = longitude;
        }
        else
        {
            errors.Add(L.T("longitude invalide (nombre entre -180 et 180)."));
        }
        if (customer.ContactMail is not null && !new EmailAddressAttribute().IsValid(customer.ContactMail))
        {
            errors.Add(L.T("e-mail « {0} » invalide.", customer.ContactMail));
        }
        return customer;
    }

    private static string? Required(string? value, string column, List<string> errors)
    {
        if (value is null)
        {
            errors.Add(L.T("la colonne « {0} » est obligatoire.", column));
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
                errors.Add(L.T("« {0} » dépasse {1} caractères.", L.T(label), maxLength.Length));
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
