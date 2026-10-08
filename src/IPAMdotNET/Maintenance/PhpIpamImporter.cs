using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Numerics;
using System.Text.Json;
using IPAMdotNet.Data;
using IPAMdotNet.Networking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IPAMdotNet.Maintenance;

public sealed class PhpIpamImportReport
{
    public List<(string Label, int Count)> Counts { get; } = [];
    public List<string> Warnings { get; } = [];

    public override string ToString() => string.Join(", ", Counts.Where(c => c.Count > 0).Select(c => $"{c.Count} {c.Label}"));
}

/// <summary>
/// Import d'une instance phpIPAM dans une installation IPAMdotNet vide, en une transaction (tout ou rien).
/// Les identifiants phpIPAM ne sont pas conservés : chaque étape construit la table de correspondance des suivantes.
/// Les données incohérentes (adresse hors de son sous-réseau, doublon…) sont ignorées et signalées.
/// </summary>
public sealed class PhpIpamImporter(AppDbContext db, PhpIpamData data, Func<string, string?> protectMailPassword)
{
    private const int BatchSize = 2000;

    private readonly PhpIpamImportReport report = new();
    private readonly Dictionary<int, int> tags = [];
    private readonly Dictionary<int, int> groups = [];
    private readonly Dictionary<int, int?> authMethods = [];
    private readonly Dictionary<int, int> users = [];
    private readonly Dictionary<int, int> locations = [];
    private readonly Dictionary<int, int> customers = [];
    private readonly Dictionary<int, int> deviceTypes = [];
    private readonly Dictionary<int, int> racks = [];
    private readonly Dictionary<int, int> devices = [];
    private readonly Dictionary<int, int> nameservers = [];
    private readonly Dictionary<int, int> vlanDomains = [];
    private readonly Dictionary<int, int> vlans = [];
    private readonly Dictionary<int, int> vrfs = [];
    private readonly Dictionary<int, int> sections = [];
    private readonly Dictionary<int, int> subnets = [];
    private readonly Dictionary<int, int> addresses = [];
    private readonly Dictionary<int, int> circuits = [];
    // Textes des objets phpIPAM référencés par les règles NAT (adresse, réseau CIDR).
    private readonly Dictionary<int, string> subnetTexts = [];
    private readonly Dictionary<int, string> addressTexts = [];
    private CancellationToken cancellationToken;

    /// <summary>L'import exige une installation sans données réseau ni infrastructure (pas de fusion).</summary>
    public static async Task<bool> IsTargetEmptyAsync(AppDbContext db) =>
        !await db.Sections.AnyAsync() && !await db.Subnets.AnyAsync() && !await db.IpAddresses.AnyAsync() && !await db.Vlans.AnyAsync()
        && !await db.Vrfs.AnyAsync() && !await db.Devices.AnyAsync() && !await db.Locations.AnyAsync() && !await db.Customers.AnyAsync();

    public async Task<PhpIpamImportReport> ImportAsync(CancellationToken cancel)
    {
        cancellationToken = cancel;
        report.Warnings.AddRange(data.Warnings);
        db.SuppressAudit = true;
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await TagsAsync();
        await GroupsAndUsersAsync();
        await LocationsAsync();
        await CustomersAsync();
        await DeviceTypesAsync();
        await RacksAsync();
        await DevicesAsync();
        await NameserversAsync();
        await VlansAsync();
        await VrfsAsync();
        await SectionsAsync();
        await DeviceSectionsAsync();
        await VlanDomainSectionsAsync();
        await SubnetsAsync();
        await AddressesAsync();
        await CircuitsAsync();
        await CustomFieldsAsync();
        await NatAsync();
        await BgpAsync();
        await PstnAsync();
        await SettingsAsync();
        await transaction.CommitAsync(cancellationToken);
        return report;
    }

    private async Task TagsAsync()
    {
        // Étiquettes 1 à 4 de phpIPAM = étiquettes système (on garde nos noms, on reprend couleurs et options).
        string?[] systemKeys = [null, Tag.OfflineKey, Tag.UsedKey, Tag.ReservedKey, Tag.DhcpKey];
        Dictionary<string, Tag> system = await db.Tags.Where(t => t.SystemKey != null).ToDictionaryAsync(t => t.SystemKey!, cancellationToken);
        HashSet<string> names = new(await db.Tags.Select(t => t.Name).ToListAsync(cancellationToken), StringComparer.OrdinalIgnoreCase);
        // Correspondance fixe, même si la table des étiquettes n'a pas pu être lue (API).
        for (int old = 1; old <= 4; old++)
        {
            if (system.TryGetValue(systemKeys[old]!, out Tag? fixedTag))
            {
                tags[old] = fixedTag.Id;
            }
        }
        List<(int, Tag)> created = [];
        foreach (Dictionary<string, string?> row in data.Rows("ipTags"))
        {
            int old = I(row, "id") ?? 0;
            Tag tag = old is >= 1 and <= 4 && system.TryGetValue(systemKeys[old]!, out Tag? existing)
                ? existing
                : new Tag { Name = Unique(S(row, "type", 50) ?? $"Étiquette {old}", names, 50) };
            tag.BackgroundColor = Color(S(row, "bgcolor")) ?? tag.BackgroundColor;
            tag.TextColor = Color(S(row, "fgcolor")) ?? tag.TextColor;
            tag.ShowTag = B(row, "showtag");
            tag.Compress = B(row, "compress");
            tag.UpdateByScan = B(row, "updateTag");
            if (tag.Id == 0)
            {
                created.Add((old, tag));
            }
            else
            {
                tags[old] = tag.Id;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        Merge(tags, await SaveAsync(created, t => t.Id));
        report.Counts.Add(("étiquette(s) personnalisée(s)", created.Count));
    }

    private async Task GroupsAndUsersAsync()
    {
        if (!data.FromDatabase)
        {
            return;
        }
        HashSet<string> groupNames = new(await db.Groups.Select(g => g.Name).ToListAsync(cancellationToken), StringComparer.OrdinalIgnoreCase);
        List<(int, Group)> newGroups = data.Rows("userGroups")
            .Select(row => (I(row, "g_id") ?? 0, new Group { Name = Unique(S(row, "g_name", 100) ?? "Groupe", groupNames, 100), Description = S(row, "g_desc", 500) }))
            .ToList();
        Merge(groups, await SaveAsync(newGroups, g => g.Id));
        report.Counts.Add(("groupe(s)", newGroups.Count));

        // Méthodes d'authentification : AD / LDAP / NetIQ → annuaire LDAP ; local → mot de passe ; autres non reprises.
        HashSet<string> methodNames = new(await db.AuthMethods.Select(a => a.Name).ToListAsync(cancellationToken), StringComparer.OrdinalIgnoreCase);
        Dictionary<int, string> unsupported = [];
        List<(int, AuthMethod)> newMethods = [];
        foreach (Dictionary<string, string?> row in data.Rows("usersAuthMethod"))
        {
            int old = I(row, "id") ?? 0;
            string type = S(row, "type")?.ToLowerInvariant() ?? "local";
            if (type == "local")
            {
                authMethods[old] = null;
                continue;
            }
            Dictionary<string, string> parameters = JsonObject(S(row, "params"));
            string? host = parameters.GetValueOrDefault("domain_controllers")?.Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (type is not ("ad" or "ldap" or "netiq") || host is null)
            {
                unsupported[old] = $"{S(row, "description") ?? type} ({type})";
                continue;
            }
            bool ssl = parameters.GetValueOrDefault("use_ssl") == "1";
            string baseDn = parameters.GetValueOrDefault("users_base_dn") is { Length: > 0 } usersDn ? usersDn : parameters.GetValueOrDefault("base_dn") ?? "";
            newMethods.Add((old, new AuthMethod
            {
                Name = Unique(S(row, "description", 100) ?? type.ToUpperInvariant(), methodNames, 100),
                Host = host.Length > 200 ? host[..200] : host,
                Port = int.TryParse(parameters.GetValueOrDefault("ad_port"), out int port) && port is > 0 and <= 65535 ? port : ssl ? 636 : 389,
                UseSsl = ssl,
                BindTemplate = Truncate(type == "ldap"
                    ? $"{(parameters.GetValueOrDefault("uid_attr") is { Length: > 0 } uid ? uid : "uid")}={{0}},{baseDn}"
                    : "{0}" + parameters.GetValueOrDefault("account_suffix"), 300)!,
                Description = "Importée de phpIPAM",
                SearchBase = Truncate(baseDn.Length > 0 ? baseDn : null, 300),
                UserFilter = type == "ldap" ? $"({(parameters.GetValueOrDefault("uid_attr") is { Length: > 0 } uidAttr ? uidAttr : "uid")}={{0}})" : null,
            }));
        }
        foreach (KeyValuePair<int, int> pair in await SaveAsync(newMethods, m => m.Id))
        {
            authMethods[pair.Key] = pair.Value;
        }
        report.Counts.Add(("méthode(s) d'authentification", newMethods.Count));

        HashSet<string> userNames = (await db.Users.Select(u => u.UserName).ToListAsync(cancellationToken)).ToHashSet();
        List<(int, User)> newUsers = [];
        Dictionary<int, HashSet<int>> memberships = [];
        int withoutPassword = 0;
        foreach (Dictionary<string, string?> row in data.Rows("users"))
        {
            string? name = S(row, "username", 100) is { } raw ? Data.User.NormalizeUserName(raw) : null;
            if (name is null)
            {
                continue;
            }
            if (!userNames.Add(name))
            {
                report.Warnings.Add($"Compte « {name} » : existe déjà ici, non importé (ses groupes ne sont pas repris).");
                continue;
            }
            int method = I(row, "authMethod") ?? 1;
            if (unsupported.TryGetValue(method, out string? methodName))
            {
                report.Warnings.Add($"Compte « {name} » : méthode d'authentification « {methodName} » non reprise, mot de passe à définir.");
            }
            int? authMethodId = authMethods.GetValueOrDefault(method);
            // Haché phpIPAM (SHA-512 crypt) conservé : accepté à la connexion, puis remplacé (voir Passwords).
            string hash = authMethodId is null && S(row, "password") is { } password && password.StartsWith("$6$", StringComparison.Ordinal) ? password : "";
            if (authMethodId is null && hash.Length == 0)
            {
                withoutPassword++;
            }
            int oldId = I(row, "id") ?? 0;
            memberships[oldId] = JsonObject(S(row, "groups")).Keys.Select(k => int.TryParse(k, out int g) && groups.TryGetValue(g, out int id) ? id : 0)
                .Where(id => id != 0).ToHashSet();
            newUsers.Add((oldId, new User
            {
                UserName = name,
                PasswordHash = hash,
                DisplayName = S(row, "real_name", 100),
                Email = S(row, "email", 200),
                IsAdmin = S(row, "role") == "Administrator",
                Enabled = !B(row, "disabled"),
                AuthMethodId = authMethodId,
            }));
        }
        Merge(users, await SaveAsync(newUsers, u => u.Id));
        // Appartenances après coup : les lots d'insertion vident le suivi, des groupes attachés seraient recréés.
        List<Group> allGroups = await db.Groups.ToListAsync(cancellationToken);
        foreach ((int oldId, HashSet<int> groupIds) in memberships.Where(m => m.Value.Count > 0).Select(m => (m.Key, m.Value)))
        {
            int userId = users[oldId];
            User user = await db.Users.Include(u => u.Groups).SingleAsync(u => u.Id == userId, cancellationToken);
            user.Groups.AddRange(allGroups.Where(g => groupIds.Contains(g.Id)));
        }
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        report.Counts.Add(("utilisateur(s)", newUsers.Count));
        if (withoutPassword > 0)
        {
            report.Warnings.Add($"{withoutPassword} compte(s) sans mot de passe utilisable : à définir dans Administration › Utilisateurs.");
        }
    }

    private async Task LocationsAsync()
    {
        List<(int, Location)> items = data.Rows("locations").Select(row =>
        {
            Location.TryNormalizeCoordinate(S(row, "lat"), 90, out string? latitude);
            Location.TryNormalizeCoordinate(S(row, "long"), 180, out string? longitude);
            return (I(row, "id") ?? 0, new Location
            {
                Name = S(row, "name", 100) ?? "Emplacement",
                Description = S(row, "description", 500),
                Address = S(row, "address", 300),
                Latitude = latitude is null || longitude is null ? null : latitude,
                Longitude = latitude is null || longitude is null ? null : longitude,
            });
        }).ToList();
        Merge(locations, await SaveAsync(items, l => l.Id));
        report.Counts.Add(("emplacement(s)", items.Count));
    }

    private async Task CustomersAsync()
    {
        EmailAddressAttribute email = new();
        List<(int, Customer)> items = data.Rows("customers").Select(row => (I(row, "id") ?? 0, new Customer
        {
            Name = S(row, "title", 100) ?? "Client",
            Address = S(row, "address", 300),
            PostCode = S(row, "postcode", 20),
            City = S(row, "city", 100),
            State = S(row, "state", 100),
            Latitude = Location.TryNormalizeCoordinate(S(row, "lat"), 90, out string? latitude) ? latitude : null,
            Longitude = Location.TryNormalizeCoordinate(S(row, "long"), 180, out string? longitude) ? longitude : null,
            ContactPerson = S(row, "contact_person", 100),
            ContactPhone = S(row, "contact_phone", 50),
            ContactMail = S(row, "contact_mail", 200) is { } mail && email.IsValid(mail) ? mail : null,
            Note = S(row, "note", 1000),
        })).ToList();
        Merge(customers, await SaveAsync(items, c => c.Id));
        report.Counts.Add(("client(s)", items.Count));
    }

    private async Task DeviceTypesAsync()
    {
        // Les types par défaut d'IPAMdotNet reprennent ceux de phpIPAM : un type de même nom est réutilisé.
        Dictionary<string, int> existing = (await db.DeviceTypes.ToListAsync(cancellationToken)).ToDictionary(t => t.Name, t => t.Id, StringComparer.OrdinalIgnoreCase);
        List<(int, DeviceType)> items = [];
        foreach (Dictionary<string, string?> row in data.Rows("deviceTypes"))
        {
            int old = I(row, "tid") ?? 0;
            string name = S(row, "tname", 100) ?? $"Type {old}";
            if (existing.TryGetValue(name, out int id))
            {
                deviceTypes[old] = id;
                continue;
            }
            existing[name] = 0;
            items.Add((old, new DeviceType { Name = name, Description = S(row, "tdescription", 500) }));
        }
        Merge(deviceTypes, await SaveAsync(items, t => t.Id));
        report.Counts.Add(("type(s) d'équipement", items.Count));
    }

    private async Task RacksAsync()
    {
        List<(int, Rack)> items = data.Rows("racks").Select(row => (I(row, "id") ?? 0, new Rack
        {
            Name = S(row, "name", 100) ?? "Rack",
            Size = Math.Clamp(I(row, "size") ?? 42, 1, 60),
            HasBack = I(row, "hasBack") == 1,
            TopDown = I(row, "topDown") == 1,
            LocationId = Map(row, "location", locations),
            CustomerId = Map(row, "customer_id", customers),
            Description = S(row, "description", 500),
        })).ToList();
        Merge(racks, await SaveAsync(items, r => r.Id));
        report.Counts.Add(("rack(s)", items.Count));
    }

    private async Task DevicesAsync()
    {
        // phpIPAM numérote la face arrière à la suite de la face avant : unité n de l'arrière = taille du rack + n.
        Dictionary<int, int> rackSizes = data.Rows("racks").Where(r => I(r, "id") is not null && I(r, "hasBack") == 1)
            .ToDictionary(r => I(r, "id")!.Value, r => I(r, "size") ?? 42);
        List<(int, Device)> items = data.Rows("devices").Select(row =>
        {
            int? raw = I(row, "rack_start");
            bool back = I(row, "rack") is int source && rackSizes.TryGetValue(source, out int rackSize) && raw > rackSize;
            if (back)
            {
                raw -= rackSizes[I(row, "rack")!.Value];
            }
            int? start = raw is int s && s is >= 1 and <= 60 ? s : null;
            int? size = I(row, "rack_size") is int z && z is >= 1 and <= 60 ? z : null;
            int? rack = Map(row, "rack", racks);
            return (I(row, "id") ?? 0, new Device
            {
                Hostname = S(row, "hostname", 100) ?? $"equipement-{I(row, "id")}",
                IpAddress = IPAddress.TryParse(S(row, "ip_addr") ?? "", out IPAddress? ip) ? ip.ToString() : null,
                DeviceTypeId = Map(row, "type", deviceTypes),
                Description = S(row, "description", 500),
                LocationId = Map(row, "location", locations),
                RackId = rack,
                RackStart = rack is null ? null : start,
                RackSize = rack is null ? null : size,
                RackFace = rack is not null && back ? RackFace.Back : RackFace.Front,
            });
        }).ToList();
        Merge(devices, await SaveAsync(items, d => d.Id));
        report.Counts.Add(("équipement(s)", items.Count));
    }

    private async Task NameserversAsync()
    {
        List<(int, Nameserver)> items = [];
        foreach (Dictionary<string, string?> row in data.Rows("nameservers"))
        {
            string[] servers = (S(row, "namesrv1") ?? "").Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries)
                .Select(s => IPAddress.TryParse(s, out IPAddress? ip) ? ip.ToString() : null).OfType<string>().ToArray();
            if (servers.Length == 0)
            {
                report.Warnings.Add($"Serveurs de noms « {S(row, "name")} » : aucune adresse valide, non importés.");
                continue;
            }
            items.Add((I(row, "id") ?? 0, new Nameserver { Name = S(row, "name", 100) ?? "DNS", Servers = Truncate(string.Join(';', servers), 500)!, Description = S(row, "description", 500) }));
        }
        Merge(nameservers, await SaveAsync(items, n => n.Id));
        report.Counts.Add(("jeu(x) de serveurs de noms", items.Count));
    }

    private async Task VlansAsync()
    {
        // Domaines L2 : « default » (créé par la migration) est réutilisé par nom, comme les autres domaines de même nom.
        Dictionary<string, int> existing = await db.VlanDomains.ToDictionaryAsync(d => d.Name, d => d.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);
        List<(int, VlanDomain)> newDomains = [];
        foreach (Dictionary<string, string?> row in data.Rows("vlanDomains"))
        {
            int old = I(row, "id") ?? 0;
            string name = S(row, "name", 100) ?? $"Domaine {old}";
            if (existing.TryGetValue(name, out int id))
            {
                vlanDomains[old] = id;
                continue;
            }
            existing[name] = 0;
            newDomains.Add((old, new VlanDomain { Name = name, Description = S(row, "description", 500) }));
        }
        Merge(vlanDomains, await SaveAsync(newDomains, d => d.Id));
        int defaultDomain = await Vlan.DefaultDomainIdAsync(db);

        // Un numéro est unique dans son domaine ; un doublon (base incohérente) est ignoré.
        HashSet<(int, int)> seen = [];
        List<(int, Vlan)> items = [];
        foreach (Dictionary<string, string?> row in data.Rows("vlans"))
        {
            int old = I(row, "vlanId") ?? I(row, "id") ?? 0;
            if (I(row, "number") is not int number || number is < 1 or > 4094)
            {
                report.Warnings.Add($"VLAN « {S(row, "name")} » : numéro {S(row, "number")} invalide, non importé.");
                continue;
            }
            int domain = Map(row, "domainId", vlanDomains) ?? defaultDomain;
            if (!seen.Add((domain, number)))
            {
                report.Warnings.Add($"VLAN {number} en double dans un même domaine L2, non importé.");
                continue;
            }
            items.Add((old, new Vlan { DomainId = domain, Number = number, Name = S(row, "name", 100) ?? $"VLAN {number}", Description = S(row, "description", 500),
                CustomerId = Map(row, "customer_id", customers) }));
        }
        Merge(vlans, await SaveAsync(items, v => v.Id));
        report.Counts.Add(("domaine(s) L2", newDomains.Count));
        report.Counts.Add(("VLAN", items.Count));
    }

    private async Task VrfsAsync()
    {
        Dictionary<string, (int Old, Vrf Vrf)> byName = new(StringComparer.OrdinalIgnoreCase);
        List<(int Old, string Name)> duplicates = [];
        foreach (Dictionary<string, string?> row in data.Rows("vrf"))
        {
            int old = I(row, "vrfId") ?? I(row, "id") ?? 0;
            string name = S(row, "name", 100) ?? $"VRF {old}";
            if (byName.ContainsKey(name))
            {
                duplicates.Add((old, name));
                continue;
            }
            byName[name] = (old, new Vrf { Name = name, RouteDistinguisher = S(row, "rd", 50), Description = S(row, "description", 500) });
        }
        Merge(vrfs, await SaveAsync(byName.Values.ToList(), v => v.Id));
        foreach ((int old, string name) in duplicates)
        {
            vrfs[old] = vrfs[byName[name].Old];
        }
        report.Counts.Add(("VRF", byName.Count));
    }

    private async Task SectionsAsync()
    {
        List<Dictionary<string, string?>> rows = data.Rows("sections");
        Dictionary<int, string> names = rows.ToDictionary(r => I(r, "id") ?? 0, r => S(r, "name") ?? "Section");
        HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);
        // Sans utilisateurs ni groupes importés (source API), l'accès par défaut reste en lecture.
        SectionAccessLevel defaultAccess = data.FromDatabase ? SectionAccessLevel.None : SectionAccessLevel.Read;
        List<(int, Section)> items = rows.Select(row =>
        {
            int old = I(row, "id") ?? 0;
            // Sous-sections phpIPAM : sections à plat ici, préfixées du nom de la section parente.
            string name = I(row, "masterSection") is int master && master != 0 && names.TryGetValue(master, out string? parent) ? $"{parent} / {names[old]}" : names[old];
            return (old, new Section { Name = Unique(name, used, 100), Description = S(row, "description", 500), DefaultAccess = defaultAccess });
        }).ToList();
        Merge(sections, await SaveAsync(items, s => s.Id));
        report.Counts.Add(("section(s)", items.Count));

        if (!data.FromDatabase)
        {
            return;
        }
        // Permissions phpIPAM : {"idGroupe":"niveau"}, 1 = lecture, 2 = écriture, 3 = administration (écriture ici).
        List<SectionPermission> permissions = [];
        foreach (Dictionary<string, string?> row in rows)
        {
            if (!sections.TryGetValue(I(row, "id") ?? 0, out int sectionId))
            {
                continue;
            }
            foreach (KeyValuePair<string, string> pair in JsonObject(S(row, "permissions")))
            {
                if (int.TryParse(pair.Key, out int group) && groups.TryGetValue(group, out int groupId) && int.TryParse(pair.Value, out int level) && level > 0)
                {
                    permissions.Add(new SectionPermission { SectionId = sectionId, GroupId = groupId, Level = level == 1 ? SectionAccessLevel.Read : SectionAccessLevel.Write });
                }
            }
        }
        db.SectionPermissions.AddRange(permissions);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        report.Counts.Add(("permission(s) de section", permissions.Count));
    }

    /// <summary>Sections où chaque équipement est visible (colonne phpIPAM « sections » : « 1;2;3 »).</summary>
    private async Task DeviceSectionsAsync()
    {
        List<Section> allSections = await db.Sections.ToListAsync(cancellationToken);
        int count = 0;
        foreach (Dictionary<string, string?> row in data.Rows("devices"))
        {
            HashSet<int> wanted = (S(row, "sections") ?? "").Split([';', ','], StringSplitOptions.RemoveEmptyEntries)
                .Select(v => int.TryParse(v, out int old) && sections.TryGetValue(old, out int id) ? id : 0).Where(id => id != 0).ToHashSet();
            if (wanted.Count == 0 || !devices.TryGetValue(I(row, "id") ?? 0, out int deviceId))
            {
                continue;
            }
            Device device = await db.Devices.Include(d => d.Sections).SingleAsync(d => d.Id == deviceId, cancellationToken);
            device.Sections.AddRange(allSections.Where(s => wanted.Contains(s.Id)));
            count++;
        }
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        report.Counts.Add(("équipement(s) limité(s) à des sections", count));
    }

    /// <summary>Sections où chaque domaine L2 est proposé (colonne phpIPAM « permissions » : « 1;2;3 »).</summary>
    private async Task VlanDomainSectionsAsync()
    {
        List<Section> allSections = await db.Sections.ToListAsync(cancellationToken);
        int count = 0;
        foreach (Dictionary<string, string?> row in data.Rows("vlanDomains"))
        {
            HashSet<int> wanted = (S(row, "permissions") ?? "").Split([';', ','], StringSplitOptions.RemoveEmptyEntries)
                .Select(v => int.TryParse(v, out int old) && sections.TryGetValue(old, out int id) ? id : 0).Where(id => id != 0).ToHashSet();
            if (wanted.Count == 0 || !vlanDomains.TryGetValue(I(row, "id") ?? 0, out int domainId))
            {
                continue;
            }
            VlanDomain domain = await db.VlanDomains.Include(d => d.Sections).SingleAsync(d => d.Id == domainId, cancellationToken);
            domain.Sections.AddRange(allSections.Where(s => wanted.Contains(s.Id) && !domain.Sections.Contains(s)));
            count++;
        }
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        report.Counts.Add(("domaine(s) L2 limité(s) à des sections", count));
    }

    private async Task SubnetsAsync()
    {
        Dictionary<(int Section, string Network), int> seen = [];
        List<(int Old, Subnet Subnet)> items = [];
        List<(int Old, (int Section, string Network) Key)> duplicates = [];
        int folders = 0;
        foreach (Dictionary<string, string?> row in data.Rows("subnets"))
        {
            int old = I(row, "id") ?? 0;
            if (B(row, "isFolder") || S(row, "subnet") is null || S(row, "mask") is null)
            {
                folders++;
                continue;
            }
            if (!sections.TryGetValue(I(row, "sectionId") ?? 0, out int sectionId))
            {
                continue;
            }
            IPAddress? address = Address(S(row, "subnet"));
            if (address is null || !Ip.TryParseNetwork($"{address}/{S(row, "mask")}", out IPNetwork network))
            {
                report.Warnings.Add($"Sous-réseau n°{old} ({S(row, "subnet")}/{S(row, "mask")}) : réseau invalide, non importé (ni ses adresses).");
                continue;
            }
            subnetTexts[old] = network.ToString();
            (int, string) key = (sectionId, network.ToString());
            if (seen.ContainsKey(key))
            {
                // phpIPAM autorise un même réseau dans plusieurs VRF d'une section ; ici il est unique : fusionné.
                duplicates.Add((old, key));
                continue;
            }
            seen[key] = old;
            Subnet subnet = new()
            {
                SectionId = sectionId,
                Description = S(row, "description", 500),
                VlanId = Map(row, "vlanId", vlans),
                VrfId = Map(row, "vrfId", vrfs),
                NameserverId = Map(row, "nameserverId", nameservers),
                LocationId = Map(row, "location", locations),
                CustomerId = Map(row, "customer_id", customers),
                AllowRequests = B(row, "allowRequests"),
                PingCheck = B(row, "pingSubnet"),
                Discover = B(row, "discoverSubnet"),
            };
            subnet.SetNetwork(network);
            items.Add((old, subnet));
        }
        Merge(subnets, await SaveAsync(items, s => s.Id));
        foreach ((int old, (int Section, string Network) key) in duplicates)
        {
            subnets[old] = subnets[seen[key]];
        }
        if (folders > 0)
        {
            report.Warnings.Add($"{folders} dossier(s) phpIPAM non repris (pas de dossiers ici) : les sous-réseaux qu'ils contiennent sont importés.");
        }
        if (duplicates.Count > 0)
        {
            report.Warnings.Add($"{duplicates.Count} sous-réseau(x) en double dans une même section (VRF différents dans phpIPAM) fusionné(s) : {string.Join(", ", duplicates.Select(d => d.Key.Network).Distinct().Take(20))}.");
        }
        report.Counts.Add(("sous-réseau(x)", items.Count));
    }

    private async Task AddressesAsync()
    {
        Dictionary<int, IPNetwork> networks = await db.Subnets.AsNoTracking()
            .Select(s => new { s.Id, s.Address, s.PrefixLength }).ToDictionaryAsync(s => s.Id, s => new IPNetwork(Ip.FromBytes(s.Address), s.PrefixLength), cancellationToken);
        int? usedTag = await db.Tags.Where(t => t.SystemKey == Tag.UsedKey).Select(t => (int?)t.Id).SingleOrDefaultAsync(cancellationToken);
        HashSet<(int, BigInteger)> seen = [];
        List<(int, IpAddress)> items = [];
        int outside = 0;
        int duplicates = 0;
        foreach (Dictionary<string, string?> row in data.Rows("ipaddresses"))
        {
            if (!subnets.TryGetValue(I(row, "subnetId") ?? 0, out int subnetId))
            {
                continue;
            }
            IPNetwork network = networks[subnetId];
            IPAddress? address = Address(S(row, "ip_addr"));
            (BigInteger first, BigInteger last) = Ip.UsableRange(network);
            BigInteger value = address is null ? -1 : Ip.ToNumber(address);
            if (address is null || !Ip.Contains(network, new IPNetwork(address, address.GetAddressBytes().Length * 8))
                || (network.BaseAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && (value < first || value > last)))
            {
                outside++;
                continue;
            }
            if (!seen.Add((subnetId, value)))
            {
                duplicates++;
                continue;
            }
            int old = I(row, "id") ?? 0;
            addressTexts[old] = address.ToString();
            items.Add((old, new IpAddress
            {
                SubnetId = subnetId,
                Address = Ip.ToBytes(address),
                Hostname = S(row, "hostname", 100),
                Description = S(row, "description", 500),
                MacAddress = IpAddress.NormalizeMac(S(row, "mac")),
                Owner = S(row, "owner", 100),
                TagId = Map(row, "state", tags) ?? usedTag,
                DeviceId = Map(row, "switch", devices),
                CustomerId = Map(row, "customer_id", customers),
                ExcludePing = B(row, "excludePing"),
                LastSeen = Date(row, "lastSeen"),
            }));
        }
        Merge(addresses, await SaveAsync(items, a => a.Id));
        if (outside > 0)
        {
            report.Warnings.Add($"{outside} adresse(s) invalide(s), hors de leur sous-réseau ou adresse réseau / diffusion : non importée(s).");
        }
        if (duplicates > 0)
        {
            report.Warnings.Add($"{duplicates} adresse(s) en double (sous-réseaux fusionnés) : non importée(s).");
        }
        report.Counts.Add(("adresse(s) IP", items.Count));
    }

    private async Task CircuitsAsync()
    {
        Dictionary<string, int> providerIds = (await db.CircuitProviders.ToListAsync(cancellationToken)).ToDictionary(p => p.Name, p => p.Id, StringComparer.OrdinalIgnoreCase);
        Dictionary<int, int> providers = [];
        List<(int, CircuitProvider)> newProviders = [];
        foreach (Dictionary<string, string?> row in data.Rows("circuitProviders"))
        {
            int old = I(row, "id") ?? 0;
            string name = S(row, "name", 100) ?? $"Fournisseur {old}";
            if (providerIds.TryGetValue(name, out int id))
            {
                providers[old] = id;
                continue;
            }
            providerIds[name] = 0;
            newProviders.Add((old, new CircuitProvider { Name = name, Contact = S(row, "contact", 200), Description = S(row, "description", 500) }));
        }
        Merge(providers, await SaveAsync(newProviders, p => p.Id));
        report.Counts.Add(("fournisseur(s) de circuits", newProviders.Count));

        // Types : réutilisés par nom (types par défaut de l'installation), sinon créés avec leur couleur.
        Dictionary<string, int> typeIds = (await db.CircuitTypes.ToListAsync(cancellationToken)).ToDictionary(t => t.Name, t => t.Id, StringComparer.OrdinalIgnoreCase);
        Dictionary<int, int> types = [];
        List<(int, CircuitType)> newTypes = [];
        foreach (Dictionary<string, string?> row in data.Rows("circuitTypes"))
        {
            int old = I(row, "id") ?? 0;
            if (S(row, "ctname", 50) is not { } name)
            {
                continue;
            }
            if (typeIds.TryGetValue(name, out int id))
            {
                types[old] = id;
                continue;
            }
            typeIds[name] = 0;
            string? color = S(row, "ctcolor");
            newTypes.Add((old, new CircuitType { Name = name, Color = color is not null && System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$") ? color : "#6c757d" }));
        }
        Merge(types, await SaveAsync(newTypes, t => t.Id));
        HashSet<(int, string)> seen = [];
        List<(int, Circuit)> items = [];
        foreach (Dictionary<string, string?> row in data.Rows("circuits"))
        {
            string? cid = S(row, "cid", 100);
            int? provider = Map(row, "provider", providers);
            if (cid is null || provider is null || !seen.Add((provider.Value, cid)))
            {
                report.Warnings.Add($"Circuit « {cid} » : fournisseur inconnu ou doublon, non importé.");
                continue;
            }
            items.Add((I(row, "id") ?? 0, new Circuit
            {
                Cid = cid,
                ProviderId = provider.Value,
                TypeId = Map(row, "type", types),
                Capacity = S(row, "capacity", 50),
                Status = Enum.TryParse(S(row, "status"), true, out CircuitStatus status) ? status : CircuitStatus.Active,
                DeviceAId = Map(row, "device1", devices),
                LocationAId = Map(row, "location1", locations),
                DeviceBId = Map(row, "device2", devices),
                LocationBId = Map(row, "location2", locations),
                CustomerId = Map(row, "customer_id", customers),
                Comment = S(row, "comment", 500),
            }));
        }
        Merge(circuits, await SaveAsync(items, c => c.Id));
        report.Counts.Add(("circuit(s)", items.Count));

        // Circuits logiques et leurs membres ordonnés (base MySQL uniquement : l'API ne les expose pas).
        ILookup<int, Dictionary<string, string?>> mapping = data.Rows("circuitsLogicalMapping").ToLookup(r => I(r, "logicalCircuit_id") ?? 0);
        HashSet<string> logicalCids = new(StringComparer.OrdinalIgnoreCase);
        List<LogicalCircuit> logical = [];
        foreach (Dictionary<string, string?> row in data.Rows("circuitsLogical"))
        {
            if (S(row, "logical_cid", 100) is not { } cid || !logicalCids.Add(cid))
            {
                continue;
            }
            LogicalCircuit item = new() { Cid = cid, Purpose = S(row, "purpose", 200), Comment = S(row, "comments", 500) };
            foreach (Dictionary<string, string?> member in mapping[I(row, "id") ?? 0].OrderBy(m => I(m, "order") ?? 0))
            {
                if (Map(member, "circ_id", circuits) is int circuitId && item.Members.All(m => m.CircuitId != circuitId))
                {
                    item.Members.Add(new LogicalCircuitMember { CircuitId = circuitId, Order = item.Members.Count + 1 });
                }
            }
            logical.Add(item);
        }
        db.LogicalCircuits.AddRange(logical);
        await db.SaveChangesAsync(cancellationToken);
        report.Counts.Add(("circuit(s) logique(s)", logical.Count));
    }

    /// <summary>Colonnes « custom_* » → champs personnalisés et leurs valeurs, pour les objets repris ici.</summary>
    private async Task CustomFieldsAsync()
    {
        (string Table, string Type, string IdColumn, Dictionary<int, int> Map)[] targets =
        [
            ("ipaddresses", nameof(IpAddress), "id", addresses), ("subnets", nameof(Subnet), "id", subnets), ("vlans", nameof(Vlan), "vlanId", vlans),
            ("vrf", nameof(Vrf), "vrfId", vrfs), ("devices", nameof(Device), "id", devices), ("racks", nameof(Rack), "id", racks),
            ("locations", nameof(Location), "id", locations), ("customers", nameof(Customer), "id", customers), ("circuits", nameof(Circuit), "id", circuits),
        ];
        HashSet<string> existing = (await db.CustomFields.Select(f => f.EntityType + "|" + f.Name).ToListAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int fieldCount = 0;
        int valueCount = 0;
        int rejected = 0;
        foreach ((string table, string type, string idColumn, Dictionary<int, int> map) in targets)
        {
            List<PhpIpamCustomField> columns = data.CustomFields.Where(c => string.Equals(c.Table, table, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach ((PhpIpamCustomField column, int order) in columns.Select((c, i) => (c, i)))
            {
                string name = Truncate(column.Column["custom_".Length..], 100)!;
                if (name.Length == 0 || !existing.Add($"{type}|{name}"))
                {
                    continue;
                }
                (CustomFieldType fieldType, string? options) = FieldType(column.SqlType);
                CustomField field = new() { EntityType = type, Name = name, Type = fieldType, Options = options, Order = order, Description = Truncate(column.Comment, 300) };
                db.CustomFields.Add(field);
                await db.SaveChangesAsync(cancellationToken);
                fieldCount++;

                List<CustomFieldValue> values = [];
                foreach (Dictionary<string, string?> row in data.Rows(table))
                {
                    if (S(row, column.Column) is not { } raw || !map.TryGetValue(I(row, idColumn) ?? I(row, "id") ?? 0, out int entityId))
                    {
                        continue;
                    }
                    string text = fieldType == CustomFieldType.Date && raw.Length > 10 ? raw[..10] : raw;
                    if (fieldType == CustomFieldType.Date && text.StartsWith("0000", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (CustomFieldForm.TryNormalize(field, text, out string? value) && value is not null)
                    {
                        values.Add(new CustomFieldValue { FieldId = field.Id, EntityId = entityId, Value = value });
                    }
                    else
                    {
                        rejected++;
                    }
                }
                foreach (CustomFieldValue[] chunk in values.Chunk(BatchSize))
                {
                    db.CustomFieldValues.AddRange(chunk);
                    await db.SaveChangesAsync(cancellationToken);
                    db.ChangeTracker.Clear();
                }
                valueCount += values.Count;
            }
        }
        report.Counts.Add(("champ(s) personnalisé(s)", fieldCount));
        report.Counts.Add(("valeur(s) de champs personnalisés", valueCount));
        if (rejected > 0)
        {
            report.Warnings.Add($"{rejected} valeur(s) de champs personnalisés non conformes à leur type : non importée(s).");
        }
    }

    private static (CustomFieldType Type, string? Options) FieldType(string sqlType)
    {
        string type = sqlType.Trim().ToLowerInvariant();
        if (type.StartsWith("enum(", StringComparison.Ordinal) || type.StartsWith("set(", StringComparison.Ordinal))
        {
            string inner = sqlType[(sqlType.IndexOf('(') + 1)..sqlType.LastIndexOf(')')];
            string[] options = inner.Split(',').Select(o => o.Trim().Trim('\'').Replace("''", "'")).Where(o => o.Length > 0).ToArray();
            return (CustomFieldType.List, Truncate(string.Join('\n', options), 2000));
        }
        return type switch
        {
            _ when type.StartsWith("tinyint(1)", StringComparison.Ordinal) || type.StartsWith("bool", StringComparison.Ordinal) || type.StartsWith("bit", StringComparison.Ordinal) => (CustomFieldType.Boolean, null),
            _ when type.Contains("int") || type.StartsWith("decimal", StringComparison.Ordinal) || type.StartsWith("float", StringComparison.Ordinal) || type.StartsWith("double", StringComparison.Ordinal) => (CustomFieldType.Number, null),
            _ when type.StartsWith("date", StringComparison.Ordinal) || type.StartsWith("timestamp", StringComparison.Ordinal) => (CustomFieldType.Date, null),
            _ when type.Contains("text") => (CustomFieldType.LongText, null),
            _ => (CustomFieldType.Text, null),
        };
    }

    private async Task NatAsync()
    {
        List<NatRule> items = [];
        foreach (Dictionary<string, string?> row in data.Rows("nat"))
        {
            List<NatRuleObject> sources = NatObjects(S(row, "src"), NatSideKind.Source);
            List<NatRuleObject> destinations = NatObjects(S(row, "dst"), NatSideKind.Destination);
            string name = S(row, "name", 100) ?? $"NAT {I(row, "id")}";
            if (sources.Count == 0 || destinations.Count == 0)
            {
                report.Warnings.Add($"Règle NAT « {name} » : source ou destination introuvable, non importée.");
                continue;
            }
            items.Add(new NatRule
            {
                Name = name,
                Type = S(row, "type")?.ToLowerInvariant() switch { "source" => NatType.Source, "destination" => NatType.Destination, _ => NatType.Static },
                Objects = [.. sources, .. destinations],
                DeviceId = Map(row, "device", devices),
                SourcePort = Port(S(row, "src_port")),
                DestinationPort = Port(S(row, "dst_port")),
                Description = S(row, "description", 500),
            });
        }
        db.NatRules.AddRange(items);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        report.Counts.Add(("règle(s) NAT", items.Count));
    }

    /// <summary>Objets NAT phpIPAM ({"ipaddresses":["12"],"subnets":["3"]}) → adresses puis réseaux, en texte et liés.</summary>
    private List<NatRuleObject> NatObjects(string? json, NatSideKind side)
    {
        List<NatRuleObject> objects = [];
        if (json is null)
        {
            return objects;
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return objects;
            }
            foreach ((string kind, Dictionary<int, string> texts, Dictionary<int, int> map) in new[] { ("ipaddresses", addressTexts, addresses), ("subnets", subnetTexts, subnets) })
            {
                if (!document.RootElement.TryGetProperty(kind, out JsonElement ids) || ids.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }
                foreach (JsonElement id in ids.EnumerateArray())
                {
                    if (int.TryParse(id.ToString(), out int old) && texts.TryGetValue(old, out string? text))
                    {
                        int? linked = map.TryGetValue(old, out int newId) ? newId : null;
                        objects.Add(new NatRuleObject
                        {
                            Side = side,
                            Text = Truncate(text, 50)!,
                            SubnetId = kind == "subnets" ? linked : null,
                            AddressId = kind == "ipaddresses" ? linked : null,
                        });
                    }
                }
            }
        }
        catch (JsonException)
        {
        }
        return objects;
    }

    private async Task BgpAsync()
    {
        List<(int, BgpPeer)> items = [];
        foreach (Dictionary<string, string?> row in data.Rows("routing_bgp"))
        {
            string name = S(row, "peer_name", 100) ?? $"BGP {I(row, "id")}";
            if (L(row, "local_as") is not long localAs || L(row, "peer_as") is not long peerAs || localAs is < 1 or > 4294967295 || peerAs is < 1 or > 4294967295
                || !IPAddress.TryParse(S(row, "local_address") ?? "", out IPAddress? local) || !IPAddress.TryParse(S(row, "peer_address") ?? "", out IPAddress? peer))
            {
                report.Warnings.Add($"Pair BGP « {name} » : AS ou adresse invalide, non importé.");
                continue;
            }
            items.Add((I(row, "id") ?? 0, new BgpPeer
            {
                Name = name,
                LocalAs = localAs,
                LocalAddress = local.ToString(),
                PeerAs = peerAs,
                PeerAddress = peer.ToString(),
                VrfId = Map(row, "vrf_id", vrfs),
                Description = S(row, "description", 500),
            }));
        }
        Dictionary<int, int> peers = await SaveAsync(items, p => p.Id);
        report.Counts.Add(("pair(s) BGP", items.Count));

        // Sous-réseaux annoncés / reçus (« routing_subnets », type « bgp »).
        HashSet<(int, int, BgpDirection)> seen = [];
        List<BgpPeerSubnet> links = [];
        foreach (Dictionary<string, string?> row in data.Rows("routing_subnets").Where(r => S(r, "type") == "bgp"))
        {
            BgpDirection direction = S(row, "direction") == "received" ? BgpDirection.Received : BgpDirection.Advertised;
            if (Map(row, "object_id", peers) is int peerId && Map(row, "subnet_id", subnets) is int subnetId && seen.Add((peerId, subnetId, direction)))
            {
                links.Add(new BgpPeerSubnet { BgpPeerId = peerId, SubnetId = subnetId, Direction = direction });
            }
        }
        db.BgpPeerSubnets.AddRange(links);
        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        report.Counts.Add(("sous-réseau(x) BGP", links.Count));
    }

    private async Task PstnAsync()
    {
        Dictionary<int, string?> tagKeys = await db.Tags.ToDictionaryAsync(t => t.Id, t => t.SystemKey, cancellationToken);
        HashSet<string> prefixesSeen = [];
        List<(int, PstnPrefix)> prefixes = [];
        foreach (Dictionary<string, string?> row in data.Rows("pstnPrefixes"))
        {
            string? prefix = PstnPrefix.Normalize(S(row, "prefix"));
            if (prefix is null || prefix.Length > 20 || !prefixesSeen.Add(prefix))
            {
                report.Warnings.Add($"Préfixe RTC « {S(row, "prefix")} » : invalide ou en double, non importé (ni ses numéros).");
                continue;
            }
            prefixes.Add((I(row, "id") ?? 0, new PstnPrefix
            {
                Prefix = prefix,
                Name = S(row, "name", 100) ?? prefix,
                Start = Math.Max(0, L(row, "start") ?? 0),
                Stop = Math.Max(0, L(row, "stop") ?? 0),
                DeviceId = Map(row, "deviceId", devices),
                Description = S(row, "description", 500),
            }));
        }
        Dictionary<int, int> prefixMap = await SaveAsync(prefixes, p => p.Id);
        HashSet<(int, long)> numbersSeen = [];
        List<(int, PstnNumber)> numbers = [];
        foreach (Dictionary<string, string?> row in data.Rows("pstnNumbers"))
        {
            if (Map(row, "prefix", prefixMap) is not int prefixId || L(row, "number") is not long number || number < 0 || !numbersSeen.Add((prefixId, number)))
            {
                continue;
            }
            string? key = Map(row, "state", tags) is int tag ? tagKeys.GetValueOrDefault(tag) : null;
            numbers.Add((I(row, "id") ?? 0, new PstnNumber
            {
                PrefixId = prefixId,
                Number = number,
                Name = S(row, "name", 100),
                Owner = S(row, "owner", 100),
                State = key switch { Tag.ReservedKey => PstnNumberState.Reserved, Tag.OfflineKey => PstnNumberState.Offline, _ => PstnNumberState.Active },
                DeviceId = Map(row, "deviceId", devices),
                Description = S(row, "description", 500),
            }));
        }
        await SaveAsync(numbers, n => n.Id);
        report.Counts.Add(("préfixe(s) RTC", prefixes.Count));
        report.Counts.Add(("numéro(s) RTC", numbers.Count));
    }

    /// <summary>Paramètres du site, du scan, de la messagerie et instructions (base de données uniquement).</summary>
    private async Task SettingsAsync()
    {
        if (data.Rows("settings").FirstOrDefault() is { } settings)
        {
            ServerSettings server = await SettingsStore.LoadAsync<ServerSettings>(db, SettingsStore.ServerPrefix);
            server.SiteTitle = S(settings, "siteTitle", 100) ?? server.SiteTitle;
            server.SiteUrl = Uri.TryCreate(S(settings, "siteURL"), UriKind.Absolute, out Uri? url) && url.ToString().Length <= 200 ? url.ToString() : server.SiteUrl;
            server.AdminName = S(settings, "siteAdminName", 100) ?? server.AdminName;
            server.AdminEmail = S(settings, "siteAdminMail", 200) is { } mail && new EmailAddressAttribute().IsValid(mail) ? mail : server.AdminEmail;
            server.EnableIpRequests = B(settings, "enableIPrequests");
            server.EnableChangelog = B(settings, "enableChangelog");
            server.HideFreeRanges = B(settings, "hideFreeRange");
            await SettingsStore.SaveAsync(db, SettingsStore.ServerPrefix, server);

            ScanSettings scan = await SettingsStore.LoadAsync<ScanSettings>(db, SettingsStore.ScanPrefix);
            scan.Parallelism = Math.Clamp(I(settings, "scanMaxThreads") ?? scan.Parallelism, 1, 256);
            await SettingsStore.SaveAsync(db, SettingsStore.ScanPrefix, scan);
            report.Counts.Add(("paramètres du site", 1));
        }
        if (data.Rows("settingsMail").FirstOrDefault() is { } mailRow && S(mailRow, "mserver") is { } host)
        {
            MailSettings mail = await SettingsStore.LoadAsync<MailSettings>(db, SettingsStore.MailPrefix);
            mail.Enabled = S(mailRow, "mtype") == "smtp";
            mail.Host = Truncate(host, 200);
            mail.Port = I(mailRow, "mport") is int port && port is > 0 and <= 65535 ? port : 25;
            mail.UseTls = S(mailRow, "msecure") is "tls" or "ssl";
            bool auth = S(mailRow, "mauth") == "yes";
            mail.UserName = auth ? S(mailRow, "muser", 200) : null;
            mail.Password = auth && S(mailRow, "mpass") is { } password ? protectMailPassword(password) : null;
            mail.FromAddress = S(mailRow, "mAdminMail", 200);
            mail.FromName = S(mailRow, "mAdminName", 100);
            await SettingsStore.SaveAsync(db, SettingsStore.MailPrefix, mail);
            report.Counts.Add(("paramètres de messagerie", 1));
        }
        if (data.Rows("instructions").Select(r => S(r, "instructions")).FirstOrDefault(t => t is not null) is { } instructions)
        {
            AppSetting? setting = await db.AppSettings.FindAsync([AppSetting.InstructionsKey], cancellationToken);
            if (setting is null)
            {
                db.AppSettings.Add(new AppSetting { Key = AppSetting.InstructionsKey, Value = instructions });
            }
            else
            {
                setting.Value = instructions;
            }
            await db.SaveChangesAsync(cancellationToken);
            report.Counts.Add(("instructions", 1));
        }
    }

    /// <summary>Insère par lots et renvoie la correspondance identifiant phpIPAM → identifiant créé.</summary>
    private async Task<Dictionary<int, int>> SaveAsync<T>(List<(int Old, T Entity)> items, Func<T, int> id) where T : class
    {
        Dictionary<int, int> map = [];
        foreach ((int Old, T Entity)[] chunk in items.Chunk(BatchSize))
        {
            db.AddRange(chunk.Select(c => c.Entity));
            await db.SaveChangesAsync(cancellationToken);
            foreach ((int old, T entity) in chunk)
            {
                map[old] = id(entity);
            }
            db.ChangeTracker.Clear();
        }
        return map;
    }

    private static void Merge(Dictionary<int, int> target, Dictionary<int, int> source)
    {
        foreach (KeyValuePair<int, int> pair in source)
        {
            target[pair.Key] = pair.Value;
        }
    }

    private static string? S(Dictionary<string, string?> row, string column, int max = int.MaxValue) =>
        Truncate(row.GetValueOrDefault(column)?.Trim() is { Length: > 0 } value ? value : null, max);

    private static string? Truncate(string? value, int max) => value is not null && value.Length > max ? value[..max] : value;

    private static int? I(Dictionary<string, string?> row, string column) =>
        int.TryParse(S(row, column), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : null;

    private static long? L(Dictionary<string, string?> row, string column) =>
        long.TryParse(S(row, column), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : null;

    /// <summary>Booléens phpIPAM : 1/0, Yes/No, true/false.</summary>
    private static bool B(Dictionary<string, string?> row, string column) => S(row, column)?.ToLowerInvariant() is "1" or "yes" or "true" or "on";

    private static int? Map(Dictionary<string, string?> row, string column, Dictionary<int, int> map) =>
        I(row, column) is int old && map.TryGetValue(old, out int id) ? id : null;

    private static int? Port(string? text) => int.TryParse(text, out int port) && port is > 0 and <= 65535 ? port : null;

    private static DateTime? Date(Dictionary<string, string?> row, string column) =>
        DateTime.TryParse(S(row, column), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal | DateTimeStyles.AdjustToUniversal, out DateTime date)
            && date.Year > 1970 ? date : null;

    /// <summary>Adresse phpIPAM : décimale en base (« 167772161 »), pointée ou IPv6 dans l'API.</summary>
    public static IPAddress? Address(string? text)
    {
        if (text is null)
        {
            return null;
        }
        if (text.Contains('.') || text.Contains(':'))
        {
            return IPAddress.TryParse(text, out IPAddress? parsed) ? parsed : null;
        }
        // Même règle que phpIPAM : au-delà de 2^32 - 1, c'est une adresse IPv6.
        return BigInteger.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out BigInteger value) && value.Sign >= 0 && value < BigInteger.One << 128
            ? Ip.FromNumber(value, ipv4: value <= uint.MaxValue)
            : null;
    }

    private static string? Color(string? text)
    {
        if (text is null || !text.StartsWith('#'))
        {
            return null;
        }
        string hex = text[1..];
        if (hex.Length == 3)
        {
            hex = string.Concat(hex.Select(c => $"{c}{c}"));
        }
        return hex.Length == 6 && hex.All(char.IsAsciiHexDigit) ? "#" + hex.ToLowerInvariant() : null;
    }

    private static string Unique(string name, HashSet<string> used, int max)
    {
        string candidate = Truncate(name, max)!;
        for (int i = 2; !used.Add(candidate); i++)
        {
            string suffix = $" ({i})";
            candidate = Truncate(name, max - suffix.Length) + suffix;
        }
        return candidate;
    }

    /// <summary>Objet JSON plat (paramètres, groupes, permissions) en dictionnaire de textes ; vide si absent ou invalide.</summary>
    private static Dictionary<string, string> JsonObject(string? json)
    {
        Dictionary<string, string> values = [];
        if (json is null)
        {
            return values;
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in document.RootElement.EnumerateObject())
                {
                    values[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? "" : property.Value.GetRawText();
                }
            }
        }
        catch (JsonException)
        {
        }
        return values;
    }
}
