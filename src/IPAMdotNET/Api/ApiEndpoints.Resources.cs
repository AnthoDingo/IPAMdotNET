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
    public Func<AppDbContext, int, ApiKey, Task<string?>>? BeforeDelete { get; init; }

    /// <summary>Filtre de lecture selon les droits (sections) ; null = lisible par toute clé.</summary>
    public Func<IQueryable<T>, SectionAccess, IQueryable<T>>? ReadFilter { get; init; }

    /// <summary>Nouvel objet (types à membres « required ») ; par défaut, constructeur sans paramètre.</summary>
    public Func<T>? Create { get; init; }

    /// <summary>Champs JSON traités par <see cref="Check"/> / <see cref="AfterSave"/> (mot de passe, membres…), hors propriétés directes.</summary>
    public string[] ExtraFields { get; init; } = [];

    /// <summary>Règles qui ont besoin de la clé appelante ou du corps JSON (champs supplémentaires), avant l'enregistrement.</summary>
    public Func<ApiCall<T>, Task>? Check { get; init; }

    /// <summary>Après l'enregistrement (identifiant connu) : données liées, ex. permissions d'un groupe.</summary>
    public Func<ApiCall<T>, Task>? AfterSave { get; init; }

    /// <summary>Propriétés calculées ajoutées à la réponse (membres, groupes…).</summary>
    public Func<AppDbContext, T, Task<Dictionary<string, object?>>>? ExtraJson { get; init; }
}

/// <summary>Contexte d'une création / modification : base, clé appelante, objet, corps JSON, erreurs à compléter.</summary>
internal sealed record ApiCall<T>(AppDbContext Db, ApiKey Key, T Item, JsonElement Body, bool Creating, Dictionary<string, string[]> Errors)
{
    /// <summary>Champ du corps JSON, sans tenir compte de la casse.</summary>
    public bool TryGet(string name, out JsonElement value)
    {
        foreach (JsonProperty property in Body.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
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
            BeforeDelete = async (db, id, key) =>
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
            BeforeDelete = async (db, id, key) => { await db.DetachLocationAsync(id); return null; },
        });
        MapResource(api, new ApiResource<Customer>
        {
            Path = "customers", Label = "Client",
            Fields = [nameof(Customer.Name), nameof(Customer.Address), nameof(Customer.PostCode), nameof(Customer.City), nameof(Customer.State),
                nameof(Customer.Latitude), nameof(Customer.Longitude), nameof(Customer.ContactPerson), nameof(Customer.ContactPhone),
                nameof(Customer.ContactMail), nameof(Customer.Note)],
            Validate = (db, c, errors) =>
            {
                AddIf(errors, !Location.TryNormalizeCoordinate(c.Latitude, 90, out string? latitude), "latitude", "Latitude invalide (nombre entre -90 et 90).");
                AddIf(errors, !Location.TryNormalizeCoordinate(c.Longitude, 180, out string? longitude), "longitude", "Longitude invalide (nombre entre -180 et 180).");
                (c.Latitude, c.Longitude) = (latitude, longitude);
                return Task.CompletedTask;
            },
            BeforeDelete = async (db, id, key) => { await db.DetachCustomerAsync(id); return null; },
        });
        MapResource(api, new ApiResource<Rack>
        {
            Path = "racks", Label = "Rack",
            Fields = [nameof(Rack.Name), nameof(Rack.Size), nameof(Rack.HasBack), nameof(Rack.TopDown), nameof(Rack.LocationId), nameof(Rack.CustomerId),
                nameof(Rack.Description)],
            Validate = async (db, r, errors) =>
            {
                int highest = r.Id == 0 ? 0 : await db.Devices.Where(d => d.RackId == r.Id && d.RackStart != null)
                    .Select(d => (int?)(d.RackStart + d.RackSize - 1)).MaxAsync() ?? 0;
                AddIf(errors, highest > r.Size, "size", $"Un équipement occupe l'unité {highest} : la hauteur ne peut pas être inférieure.");
                AddIf(errors, !r.HasBack && r.Id != 0 && await db.Devices.AnyAsync(d => d.RackId == r.Id && d.RackFace == RackFace.Back), "hasBack",
                    "Des équipements sont placés en face arrière : déplacez-les d'abord.");
            },
            BeforeDelete = async (db, id, key) => { await db.DetachRackAsync(id); return null; },
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
        MapResource(api, new ApiResource<VlanDomain>
        {
            Path = "vlan-domains", Label = "Domaine L2", Fields = [nameof(VlanDomain.Name), nameof(VlanDomain.Description)],
            ExtraFields = ["sectionIds"],
            Validate = async (db, d, errors) =>
                AddIf(errors, await db.VlanDomains.AnyAsync(x => x.Name == d.Name && x.Id != d.Id), "name", "Un domaine L2 porte déjà ce nom."),
            // Sections ouvertes, remplacées en bloc (comme le formulaire) ; [] = toutes les sections.
            Check = async call =>
            {
                if (call.TryGet("sectionIds", out JsonElement json)
                    && await IdsAsync(call.Db.Sections, json, "sectionIds", "Section inexistante.", call.Errors) is { } sections)
                {
                    if (!call.Creating)
                    {
                        await call.Db.Entry(call.Item).Collection(d => d.Sections).LoadAsync();
                    }
                    call.Item.Sections.Clear();
                    call.Item.Sections.AddRange(sections);
                }
            },
            ExtraJson = async (db, d) => new Dictionary<string, object?>
            {
                ["sectionIds"] = await db.VlanDomains.Where(x => x.Id == d.Id).SelectMany(x => x.Sections).Select(s => s.Id).OrderBy(id => id).ToListAsync(),
            },
            BeforeDelete = (db, id, key) => Pages.Network.VlanDomains.EditModel.DeleteRefusalAsync(db, id),
        });
        MapResource(api, new ApiResource<DeviceType>
        {
            Path = "device-types", Label = "Type d'équipement", Fields = [nameof(DeviceType.Name), nameof(DeviceType.Description)],
            Validate = async (db, t, errors) =>
                AddIf(errors, await db.DeviceTypes.AnyAsync(x => x.Name == t.Name && x.Id != t.Id), "name", "Ce type existe déjà."),
            BeforeDelete = async (db, id, key) => { await db.DetachDeviceTypeAsync(id); return null; },
        });
        MapResource(api, new ApiResource<CircuitProvider>
        {
            Path = "circuit-providers", Label = "Fournisseur",
            Fields = [nameof(CircuitProvider.Name), nameof(CircuitProvider.Contact), nameof(CircuitProvider.Description)],
            Validate = async (db, p, errors) =>
                AddIf(errors, await db.CircuitProviders.AnyAsync(x => x.Name == p.Name && x.Id != p.Id), "name", "Un fournisseur porte déjà ce nom."),
            BeforeDelete = async (db, id, key) =>
                await db.Circuits.AnyAsync(c => c.ProviderId == id) ? "Impossible de supprimer un fournisseur qui a des circuits." : null,
        });
        MapResource(api, new ApiResource<Circuit>
        {
            Path = "circuits", Label = "Circuit",
            Fields = [nameof(Circuit.Cid), nameof(Circuit.ProviderId), nameof(Circuit.TypeId), nameof(Circuit.Capacity), nameof(Circuit.Status),
                nameof(Circuit.DeviceAId), nameof(Circuit.LocationAId), nameof(Circuit.DeviceBId), nameof(Circuit.LocationBId), nameof(Circuit.CustomerId),
                nameof(Circuit.Comment)],
            Validate = async (db, c, errors) =>
                AddIf(errors, await db.Circuits.AnyAsync(x => x.ProviderId == c.ProviderId && x.Cid == c.Cid && x.Id != c.Id), "cid",
                    "Ce fournisseur a déjà un circuit avec cet identifiant."),
        });
        MapResource(api, new ApiResource<CircuitType>
        {
            Path = "circuit-types", Label = "Type de circuit",
            Fields = [nameof(CircuitType.Name), nameof(CircuitType.Color), nameof(CircuitType.Description)],
            Validate = async (db, t, errors) =>
                AddIf(errors, await db.CircuitTypes.AnyAsync(x => x.Name == t.Name && x.Id != t.Id), "name", "Un type porte déjà ce nom."),
            BeforeDelete = async (db, id, key) => { await db.DetachCircuitTypeAsync(id); return null; },
        });
        MapResource(api, new ApiResource<LogicalCircuit>
        {
            Path = "logical-circuits", Label = "Circuit logique",
            Fields = [nameof(LogicalCircuit.Cid), nameof(LogicalCircuit.Purpose), nameof(LogicalCircuit.Comment)],
            Validate = async (db, l, errors) =>
                AddIf(errors, await db.LogicalCircuits.AnyAsync(x => x.Cid == l.Cid && x.Id != l.Id), "cid", "Un circuit logique porte déjà cet identifiant."),
            ExtraFields = ["circuitIds"],
            // Membres remplacés en bloc, dans l'ordre de la liste envoyée.
            Check = async call =>
            {
                if (!call.TryGet("circuitIds", out JsonElement json) || await IdsAsync(call.Db.Circuits, json, "circuitIds", "Circuit inexistant.", call.Errors) is null)
                {
                    return;
                }
                List<int> ordered = json.ValueKind == JsonValueKind.Array ? [.. json.EnumerateArray().Select(e => e.GetInt32()).Distinct()] : [];
                if (!call.Creating && !call.Db.Entry(call.Item).Collection(l => l.Members).IsLoaded)
                {
                    await call.Db.Entry(call.Item).Collection(l => l.Members).LoadAsync();
                }
                call.Item.SetMembers(call.Db, ordered);
            },
            ExtraJson = async (db, l) => new Dictionary<string, object?>
            {
                ["circuitIds"] = await db.LogicalCircuitMembers.Where(m => m.LogicalCircuitId == l.Id).OrderBy(m => m.Order).Select(m => m.CircuitId).ToListAsync(),
            },
        });
        MapResource(api, new ApiResource<NatRule>
        {
            Path = "nat", Label = "Règle NAT",
            Fields = [nameof(NatRule.Name), nameof(NatRule.Type), nameof(NatRule.SourcePort), nameof(NatRule.DestinationPort),
                nameof(NatRule.DeviceId), nameof(NatRule.Description)],
            ExtraFields = ["source", "destination"],
            Check = async call =>
            {
                List<NatRuleObject>? sources = await NatObjectsAsync(call, "source", NatSideKind.Source);
                List<NatRuleObject>? destinations = await NatObjectsAsync(call, "destination", NatSideKind.Destination);
                if (sources is null && destinations is null)
                {
                    return;
                }
                if (!call.Creating)
                {
                    await call.Db.Entry(call.Item).Collection(n => n.Objects).LoadAsync();
                }
                // Côté envoyé : remplacé en bloc ; côté absent : inchangé.
                foreach ((NatSideKind side, List<NatRuleObject>? replacement) in new[] { (NatSideKind.Source, sources), (NatSideKind.Destination, destinations) })
                {
                    if (replacement is not null)
                    {
                        call.Db.NatRuleObjects.RemoveRange(call.Item.Objects.Where(o => o.Side == side).ToList());
                        call.Item.Objects.RemoveAll(o => o.Side == side);
                        call.Item.Objects.AddRange(replacement);
                    }
                }
            },
            ExtraJson = async (db, n) =>
            {
                List<NatRuleObject> objects = await db.NatRuleObjects.Where(o => o.NatRuleId == n.Id).OrderBy(o => o.Id).ToListAsync();
                object Json(NatSideKind side) => objects.Where(o => o.Side == side).Select(o => new { text = o.Text, subnetId = o.SubnetId, addressId = o.AddressId });
                return new Dictionary<string, object?> { ["source"] = Json(NatSideKind.Source), ["destination"] = Json(NatSideKind.Destination) };
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
            ExtraFields = ["advertisedSubnetIds", "receivedSubnetIds"],
            // Sous-réseaux d'un sens remplacés en bloc quand la liste est envoyée.
            Check = async call =>
            {
                foreach ((string field, BgpDirection direction) in new[] { ("advertisedSubnetIds", BgpDirection.Advertised), ("receivedSubnetIds", BgpDirection.Received) })
                {
                    if (!call.TryGet(field, out JsonElement json)
                        || await IdsAsync(call.Db.Subnets, json, field, "Sous-réseau inexistant.", call.Errors) is not { } subnets)
                    {
                        continue;
                    }
                    if (!call.Creating && !call.Db.Entry(call.Item).Collection(p => p.Subnets).IsLoaded)
                    {
                        await call.Db.Entry(call.Item).Collection(p => p.Subnets).LoadAsync();
                    }
                    List<BgpPeerSubnet> removed = call.Item.Subnets.Where(x => x.Direction == direction && !subnets.Any(s => s.Id == x.SubnetId)).ToList();
                    call.Db.BgpPeerSubnets.RemoveRange(removed);
                    call.Item.Subnets.RemoveAll(removed.Contains);
                    call.Item.Subnets.AddRange(subnets.Where(s => !call.Item.Subnets.Any(x => x.Direction == direction && x.SubnetId == s.Id))
                        .Select(s => new BgpPeerSubnet { SubnetId = s.Id, Direction = direction }));
                }
            },
            ExtraJson = async (db, p) =>
            {
                List<BgpPeerSubnet> links = await db.BgpPeerSubnets.Where(x => x.BgpPeerId == p.Id).OrderBy(x => x.SubnetId).ToListAsync();
                return new Dictionary<string, object?>
                {
                    ["advertisedSubnetIds"] = links.Where(x => x.Direction == BgpDirection.Advertised).Select(x => x.SubnetId).ToList(),
                    ["receivedSubnetIds"] = links.Where(x => x.Direction == BgpDirection.Received).Select(x => x.SubnetId).ToList(),
                };
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

        MapResource(api, new ApiResource<Tag>
        {
            Path = "tags", Label = "Étiquette",
            Fields = [nameof(Tag.Name), nameof(Tag.Description), nameof(Tag.BackgroundColor), nameof(Tag.TextColor), nameof(Tag.ShowTag),
                nameof(Tag.Compress), nameof(Tag.UpdateByScan)],
            Validate = async (db, t, errors) =>
                AddIf(errors, await db.Tags.AnyAsync(x => x.Name == t.Name && x.Id != t.Id), "name", "Une étiquette porte déjà ce nom."),
            // Clé système et verrouillage : en lecture seule.
            ExtraJson = (db, t) => Task.FromResult(new Dictionary<string, object?> { ["systemKey"] = t.SystemKey, ["locked"] = t.Locked }),
            BeforeDelete = async (db, id, key) =>
                await db.Tags.AnyAsync(t => t.Id == id && t.Locked) ? "Une étiquette système ne peut pas être supprimée." : null,
        });
        MapResource(api, new ApiResource<CustomField>
        {
            Path = "custom-fields", Label = "Champ personnalisé",
            Fields = [nameof(CustomField.EntityType), nameof(CustomField.Name), nameof(CustomField.Type), nameof(CustomField.Options),
                nameof(CustomField.Required), nameof(CustomField.Order), nameof(CustomField.Description)],
            Validate = async (db, f, errors) =>
            {
                AddIf(errors, !CustomField.SupportedTypes.Contains(f.EntityType), "entityType",
                    $"Type d'objet inconnu (possibles : {string.Join(", ", CustomField.SupportedTypes)}).");
                AddIf(errors, f.Id != 0 && await db.CustomFields.AnyAsync(x => x.Id == f.Id && x.EntityType != f.EntityType), "entityType",
                    "Le type d'objet d'un champ existant ne peut pas changer.");
                AddIf(errors, await db.CustomFields.AnyAsync(x => x.EntityType == f.EntityType && x.Name == f.Name && x.Id != f.Id), "name",
                    "Ce type d'objet a déjà un champ de ce nom.");
                // Liste : un choix par ligne, sans doublon ; autres types : pas de choix.
                f.Options = f.Type == CustomFieldType.List ? string.Join('\n', f.OptionList.Distinct()) : null;
                AddIf(errors, f.Type == CustomFieldType.List && f.OptionList.Length == 0, "options", "Une liste doit proposer au moins un choix (un par ligne).");
            },
        });
        MapResource(api, new ApiResource<Group>
        {
            Path = "groups", Label = "Groupe", Fields = [nameof(Group.Name), nameof(Group.Description)],
            ExtraFields = ["memberIds", "permissions"],
            ReadFilter = (query, access) => access.IsAdmin ? query : query.Where(_ => false),
            Validate = async (db, g, errors) =>
                AddIf(errors, await db.Groups.AnyAsync(x => x.Name == g.Name && x.Id != g.Id), "name", "Un groupe porte déjà ce nom."),
            Check = async call =>
            {
                if (call.TryGet("memberIds", out JsonElement members))
                {
                    if (await IdsAsync(call.Db.Users, members, "memberIds", "Utilisateur inexistant.", call.Errors) is { } users)
                    {
                        if (!call.Creating)
                        {
                            await call.Db.Entry(call.Item).Collection(g => g.Users).LoadAsync();
                        }
                        call.Item.Users.Clear();
                        call.Item.Users.AddRange(users);
                    }
                }
                if (call.TryGet("permissions", out JsonElement permissions))
                {
                    await ParsePermissionsAsync(call.Db, permissions, call.Errors);
                }
            },
            // Permissions remplacées en bloc quand elles sont envoyées (comme le formulaire) ; « None » = pas de ligne.
            AfterSave = async call =>
            {
                if (!call.TryGet("permissions", out JsonElement permissions)
                    || await ParsePermissionsAsync(call.Db, permissions, []) is not { } levels)
                {
                    return;
                }
                await call.Db.SectionPermissions.Where(p => p.GroupId == call.Item.Id).ExecuteDeleteAsync();
                call.Db.SectionPermissions.AddRange(levels.Where(l => l.Value != SectionAccessLevel.None)
                    .Select(l => new SectionPermission { GroupId = call.Item.Id, SectionId = l.Key, Level = l.Value }));
                await call.Db.SaveChangesAsync();
            },
            ExtraJson = async (db, g) => new Dictionary<string, object?>
            {
                ["memberIds"] = await db.Groups.Where(x => x.Id == g.Id).SelectMany(x => x.Users).Select(u => u.Id).OrderBy(id => id).ToListAsync(),
                ["permissions"] = await db.SectionPermissions.Where(p => p.GroupId == g.Id)
                    .ToDictionaryAsync(p => p.SectionId.ToString(), p => p.Level.ToString()),
            },
        });
        MapResource(api, new ApiResource<User>
        {
            Path = "users", Label = "Utilisateur",
            Fields = [nameof(User.UserName), nameof(User.DisplayName), nameof(User.Email), nameof(User.IsAdmin), nameof(User.Enabled), nameof(User.AuthMethodId)],
            ExtraFields = ["password", "groupIds"],
            Create = () => new User { UserName = "", PasswordHash = "" },
            ReadFilter = (query, access) => access.IsAdmin ? query : query.Where(_ => false),
            Check = async call =>
            {
                User user = call.Item;
                AppDbContext db = call.Db;
                user.UserName = User.NormalizeUserName(user.UserName ?? "");
                AddIf(call.Errors, user.UserName.Length == 0, "userName", "Le nom d'utilisateur est requis.");
                AddIf(call.Errors, user.UserName.Length > 100, "userName", "100 caractères au plus.");
                AddIf(call.Errors, await db.Users.AnyAsync(u => u.UserName == user.UserName && u.Id != user.Id), "userName", "Ce nom d'utilisateur existe déjà.");
                AddIf(call.Errors, user.Email is not null && !new EmailAddressAttribute().IsValid(user.Email), "email", "Adresse e-mail invalide.");

                // Mot de passe (écriture seule) : comptes locaux uniquement ; un compte d'annuaire n'en a pas.
                string? password = call.TryGet("password", out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                if (user.AuthMethodId is not null)
                {
                    user.PasswordHash = "";
                }
                else if (!string.IsNullOrEmpty(password))
                {
                    AddIf(call.Errors, password.Length < Pages.Administration.Users.EditModel.MinPasswordLength, "password",
                        $"Le mot de passe doit faire au moins {Pages.Administration.Users.EditModel.MinPasswordLength} caractères.");
                    user.PasswordHash = Passwords.Hash(user, password);
                }
                else
                {
                    AddIf(call.Errors, string.IsNullOrEmpty(user.PasswordHash), "password", "Mot de passe requis pour un compte local.");
                }

                // Comme le formulaire : on ne se retire pas ses propres droits, et il reste un administrateur actif.
                User? original = call.Creating ? null : await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
                bool losesAdmin = original is { IsAdmin: true, Enabled: true } && (!user.IsAdmin || !user.Enabled);
                AddIf(call.Errors, losesAdmin && call.Key.UserId == user.Id, "isAdmin",
                    "Une clé ne peut pas retirer les droits d'administration de son propre compte ni le désactiver.");
                AddIf(call.Errors, losesAdmin && call.Key.UserId != user.Id && !await db.Users.AnyAsync(u => u.Id != user.Id && u.IsAdmin && u.Enabled),
                    "isAdmin", "Il doit rester au moins un administrateur actif.");

                if (call.TryGet("groupIds", out JsonElement groupIds)
                    && await IdsAsync(db.Groups, groupIds, "groupIds", "Groupe inexistant.", call.Errors) is { } groups)
                {
                    if (!call.Creating)
                    {
                        await db.Entry(user).Collection(u => u.Groups).LoadAsync();
                    }
                    user.Groups.Clear();
                    user.Groups.AddRange(groups);
                }
            },
            ExtraJson = async (db, u) => new Dictionary<string, object?>
            {
                ["groupIds"] = await db.Users.Where(x => x.Id == u.Id).SelectMany(x => x.Groups).Select(g => g.Id).OrderBy(id => id).ToListAsync(),
            },
            BeforeDelete = async (db, id, key) =>
                key.UserId == id ? "Une clé ne peut pas supprimer son propre compte."
                : await db.Users.AnyAsync(u => u.Id == id && u.IsAdmin && u.Enabled) && !await db.Users.AnyAsync(u => u.Id != id && u.IsAdmin && u.Enabled)
                    ? "Il doit rester au moins un administrateur actif."
                : await db.IpRequests.AnyAsync(r => r.RequestedById == id) ? "Cet utilisateur a des demandes d'adresses : désactivez-le plutôt que de le supprimer."
                : null,
        });
    }

    private static void MapResource<T>(RouteGroupBuilder api, ApiResource<T> resource) where T : class
    {
        string entityType = typeof(T).Name;

        api.MapGet($"/{resource.Path}", async (HttpContext http, AppDbContext db) =>
        {
            List<T> items = await Readable(db, resource, Context(http).Access).ToListAsync();
            Dictionary<int, Dictionary<string, string>> custom = await CustomFieldsByIdAsync(db, entityType);
            List<Dictionary<string, object?>> json = [];
            foreach (T item in items)
            {
                json.Add(await ToJsonAsync(db, resource, item, custom));
            }
            return Results.Ok(json.OrderBy(i => (int)i["id"]!));
        });

        api.MapGet($"/{resource.Path}/{{id:int}}", async (HttpContext http, AppDbContext db, int id) =>
        {
            T? item = await Readable(db, resource, Context(http).Access).SingleOrDefaultAsync(e => EF.Property<int>(e, "Id") == id);
            return item is null ? NotFound(resource.Label, id) : Results.Ok(await ToJsonAsync(db, resource, item, await CustomFieldsByIdAsync(db, entityType)));
        });

        api.MapPost($"/{resource.Path}", async (HttpContext http, AppDbContext db, JsonElement body) =>
        {
            if (AdminWriteDenied(Context(http)) is { } denied)
            {
                return denied;
            }
            T item = resource.Create?.Invoke() ?? Activator.CreateInstance<T>();
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
            if (resource.BeforeDelete is not null && await resource.BeforeDelete(db, id, Context(http).Key) is { } refusal)
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

    /// <summary>Liste JSON d'identifiants → objets existants ; null (et erreur) si invalide ou si un identifiant n'existe pas.</summary>
    /// <summary>
    /// Un côté d'une règle NAT (null s'il n'est pas envoyé) : liste d'éléments, chacun une chaîne (adresse ou réseau, liée
    /// automatiquement à l'objet correspondant s'il est seul), ou un objet { "subnetId" } / { "addressId" } (objet imposé)
    /// ou { "text" } (adresse externe, sans lien).
    /// </summary>
    private static async Task<List<NatRuleObject>?> NatObjectsAsync(ApiCall<NatRule> call, string field, NatSideKind side)
    {
        if (!call.TryGet(field, out JsonElement json))
        {
            AddIf(call.Errors, call.Creating, field, "Liste requise, ex. [\"10.0.0.1\"].");
            return null;
        }
        if (json.ValueKind != JsonValueKind.Array || json.GetArrayLength() == 0)
        {
            AddIf(call.Errors, true, field, "Liste d'au moins une adresse ou un réseau attendue, ex. [\"10.0.0.1\", {\"subnetId\": 3}].");
            return null;
        }
        List<NatRuleObject> objects = [];
        int index = 0;
        foreach (JsonElement element in json.EnumerateArray())
        {
            string key = $"{field}[{index++}]";
            (string? text, string? choice) = element.ValueKind switch
            {
                JsonValueKind.String => (element.GetString(), null),
                JsonValueKind.Object when element.TryGetProperty("addressId", out JsonElement a) && a.TryGetInt32(out int addressId) => (null, $"a:{addressId}"),
                JsonValueKind.Object when element.TryGetProperty("subnetId", out JsonElement s) && s.TryGetInt32(out int subnetId) => (null, $"s:{subnetId}"),
                JsonValueKind.Object when element.TryGetProperty("text", out JsonElement t) => (t.GetString(), NatLinks.None),
                _ => ((string?)null, (string?)null),
            };
            NatSide resolved = await NatLinks.ResolveAsync(call.Db, text, choice);
            if (resolved.Error is not null)
            {
                string candidates = resolved.Candidates is null ? ""
                    : " " + string.Join(" ; ", resolved.Candidates.Select(c => $"{{\"{(c.Key[0] == 'a' ? "addressId" : "subnetId")}\": {c.Key[2..]}}} : {c.Label}"));
                AddIf(call.Errors, true, key, resolved.Error + candidates);
                continue;
            }
            objects.Add(new NatRuleObject { Side = side, Text = resolved.Text, SubnetId = resolved.SubnetId, AddressId = resolved.AddressId });
        }
        return objects;
    }

    private static async Task<List<TEntity>?> IdsAsync<TEntity>(DbSet<TEntity> set, JsonElement json, string field, string missing,
        Dictionary<string, string[]> errors) where TEntity : class
    {
        int[]? ids = null;
        try
        {
            ids = json.ValueKind == JsonValueKind.Array ? json.Deserialize<int[]>() : null;
        }
        catch (JsonException)
        {
        }
        if (ids is null)
        {
            errors.TryAdd(field, ["Liste d'identifiants attendue, ex. [1, 2]."]);
            return null;
        }
        List<TEntity> found = await set.Where(e => ids.Contains(EF.Property<int>(e, "Id"))).ToListAsync();
        if (found.Count != ids.Distinct().Count())
        {
            errors.TryAdd(field, [missing]);
            return null;
        }
        return found;
    }

    /// <summary>Permissions d'un groupe : { "idSection": "None" | "Read" | "Write" } ; null (et erreur) si invalide.</summary>
    private static async Task<Dictionary<int, SectionAccessLevel>?> ParsePermissionsAsync(AppDbContext db, JsonElement json, Dictionary<string, string[]> errors)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            errors.TryAdd("permissions", ["Objet attendu, ex. { \"1\": \"Read\", \"2\": \"Write\" }."]);
            return null;
        }
        HashSet<int> sections = (await db.Sections.Select(s => s.Id).ToListAsync()).ToHashSet();
        Dictionary<int, SectionAccessLevel> levels = [];
        foreach (JsonProperty property in json.EnumerateObject())
        {
            SectionAccessLevel level = SectionAccessLevel.None;
            bool valid = int.TryParse(property.Name, out int sectionId) && sections.Contains(sectionId);
            try
            {
                valid = valid && (level = property.Value.Deserialize<SectionAccessLevel>(ValueOptions)) is var parsed && Enum.IsDefined(parsed);
            }
            catch (JsonException)
            {
                valid = false;
            }
            if (!valid)
            {
                errors.TryAdd($"permissions.{property.Name}", ["Section inexistante ou niveau invalide (None, Read, Write)."]);
                continue;
            }
            levels[sectionId] = level;
        }
        return errors.Keys.Any(k => k.StartsWith("permissions", StringComparison.Ordinal)) ? null : levels;
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
            if (string.Equals(property.Name, "id", StringComparison.OrdinalIgnoreCase)
                || resource.ExtraFields.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
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
        ApiCall<T> call = new(db, Context(http).Key, item, body, creating, errors);
        if (resource.Check is not null)
        {
            await resource.Check(call);
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
        if (resource.AfterSave is not null)
        {
            await resource.AfterSave(call);
        }
        Dictionary<string, object?> json = await ToJsonAsync(db, resource, item, await CustomFieldsByIdAsync(db, typeof(T).Name));
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

    private static async Task<Dictionary<string, object?>> ToJsonAsync<T>(AppDbContext db, ApiResource<T> resource, T item,
        Dictionary<int, Dictionary<string, string>> custom) where T : class
    {
        Dictionary<string, object?> json = ToJson(resource, item, custom);
        if (resource.ExtraJson is not null)
        {
            foreach (KeyValuePair<string, object?> extra in await resource.ExtraJson(db, item))
            {
                json[extra.Key] = extra.Value;
            }
        }
        return json;
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
