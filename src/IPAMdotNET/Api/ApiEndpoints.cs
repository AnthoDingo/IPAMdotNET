using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Numerics;
using System.Text.Json;
using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using IPAMdotNet.Networking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IPAMdotNet.Api;

public sealed record SubnetDto(int Id, int SectionId, string? Section, string Network, string? Description, int? Vlan, string? Vrf,
    string? Location, string? Customer, IReadOnlyDictionary<string, string>? CustomFields = null);
public sealed record AddressDto(int Id, int SubnetId, string? Subnet, string Address, string? Hostname, string? Description,
    string? MacAddress, string? Owner, string? Tag, string? Device, DateTime? LastSeen, IReadOnlyDictionary<string, string>? CustomFields = null);
public sealed record VlanDto(int Id, int Number, string Name, string? Description, IReadOnlyDictionary<string, string>? CustomFields = null);
public sealed record VrfDto(int Id, string Name, string? RouteDistinguisher, string? Description, IReadOnlyDictionary<string, string>? CustomFields = null);
public sealed record DeviceDto(int Id, string Hostname, string? IpAddress, string? Type, string? Location, string? Rack, int? RackStart, int? RackSize,
    string? Description, int[] SectionIds, IReadOnlyDictionary<string, string>? CustomFields = null);
public sealed record SearchDto(List<SubnetDto> Subnets, List<AddressDto> Addresses, List<DeviceDto> Devices);
public sealed record FirstFreeDto(string Address);

// Corps des créations (POST) et modifications (PATCH). En PATCH, un champ absent ou null reste inchangé ;
// une chaîne vide efface la valeur, un identifiant 0 retire la référence (VLAN, VRF, équipement…).
public sealed record SubnetInput(int? SectionId, string? Network, string? Description, int? VlanId, int? VrfId, int? LocationId, int? CustomerId,
    bool? PingCheck, bool? Discover, bool? AllowRequests, IReadOnlyDictionary<string, JsonElement>? CustomFields = null);
public sealed record AddressInput(int? SubnetId, string? Address, string? Hostname, string? Description, string? MacAddress, string? Owner, string? Tag,
    int? DeviceId, bool? ExcludePing, IReadOnlyDictionary<string, JsonElement>? CustomFields = null);
public sealed record VlanInput(int? Number, string? Name, string? Description, IReadOnlyDictionary<string, JsonElement>? CustomFields = null);
public sealed record VrfInput(string? Name, string? RouteDistinguisher, string? Description, IReadOnlyDictionary<string, JsonElement>? CustomFields = null);
public sealed record DeviceInput(string? Hostname, string? IpAddress, int? DeviceTypeId, int? LocationId, int? CustomerId, string? Description, int[]? SectionIds, IReadOnlyDictionary<string, JsonElement>? CustomFields = null);

/// <summary>
/// API REST authentifiée par clé d'API (Administration › API). Une clé prend les droits de son utilisateur (ou tous) ;
/// l'écriture exige une clé « écriture » et le droit d'écriture sur la section (droits d'admin pour VLAN, VRF et équipements).
/// Un objet illisible répond 404, comme s'il n'existait pas.
/// </summary>
public static partial class ApiEndpoints
{
    public const string RateLimitPolicy = "api";

    private const string ContextItem = "ipam.api";

    private sealed record ApiContext(ApiKey Key, SectionAccess Access);

    public static void MapIpamApi(this WebApplication app)
    {
        RouteGroupBuilder api = app.MapGroup("/api")
            .AddEndpointFilter(RequireApiKeyAsync)
            .RequireRateLimiting(RateLimitPolicy)
            .DisableAntiforgery();

        MapReads(api);
        MapSubnetWrites(api);
        MapAddressWrites(api);
        MapAdminWrites(api);
        MapResources(api);
    }

    private static void MapReads(RouteGroupBuilder api)
    {
        api.MapGet("/subnets", async (HttpContext http, AppDbContext db, int? section, string? ip) =>
        {
            IQueryable<Subnet> query = Context(http).Access.Readable(Subnets(db));
            if (section is not null)
            {
                query = query.Where(s => s.SectionId == section);
            }
            List<Subnet> subnets = await query.OrderBy(s => s.SectionId).ThenBy(s => s.Address).ThenBy(s => s.PrefixLength).ToListAsync();
            if (ip is not null)
            {
                if (!IPAddress.TryParse(ip, out IPAddress? address))
                {
                    return Results.BadRequest(new { error = "Paramètre « ip » : adresse IP invalide." });
                }
                IPNetwork host = new(address, address.GetAddressBytes().Length * 8);
                subnets = subnets.Where(s => Ip.Contains(s.Network, host)).ToList();
            }
            return Results.Ok(subnets.Select(s => ToDto(s)));
        });

        api.MapGet("/subnets/{id:int}", async (HttpContext http, AppDbContext db, int id) =>
        {
            Subnet? subnet = await Context(http).Access.Readable(Subnets(db)).SingleOrDefaultAsync(s => s.Id == id);
            if (subnet is null)
            {
                return SubnetNotFound(id);
            }
            Dictionary<string, string> custom = await db.CustomFieldValues
                .Where(v => v.EntityId == id && v.Field!.EntityType == nameof(Subnet))
                .ToDictionaryAsync(v => v.Field!.Name, v => v.Value);
            return Results.Ok(ToDto(subnet, custom));
        });

        api.MapGet("/subnets/{id:int}/addresses", async (HttpContext http, AppDbContext db, int id) =>
            await Context(http).Access.Readable(db.Subnets).AnyAsync(s => s.Id == id)
                ? Results.Ok(await WithCustomFieldsAsync(db, await Addresses(db).Where(a => a.SubnetId == id).OrderBy(a => a.Address).ToListAsync(),
                    db.IpAddresses.Where(a => a.SubnetId == id).Select(a => a.Id)))
                : SubnetNotFound(id));

        // Première adresse attribuable libre (comme « first_free » de phpIPAM).
        api.MapGet("/subnets/{id:int}/first-free", async (HttpContext http, AppDbContext db, int id) =>
        {
            Subnet? subnet = await Context(http).Access.Readable(db.Subnets).SingleOrDefaultAsync(s => s.Id == id);
            if (subnet is null)
            {
                return SubnetNotFound(id);
            }
            IPAddress? free = await FirstFreeAsync(db, subnet);
            return free is null ? Results.Conflict(new { error = $"Aucune adresse libre dans {subnet.Network}." }) : Results.Ok(new FirstFreeDto(free.ToString()));
        });

        api.MapGet("/addresses", async (HttpContext http, AppDbContext db, string? ip, string? hostname) =>
        {
            IQueryable<Subnet> readable = Context(http).Access.Readable(db.Subnets);
            IQueryable<IpAddress> query = Addresses(db).Where(a => readable.Any(s => s.Id == a.SubnetId));
            if (ip is not null)
            {
                if (!IPAddress.TryParse(ip, out IPAddress? address))
                {
                    return Results.BadRequest(new { error = "Paramètre « ip » : adresse IP invalide." });
                }
                byte[] bytes = Ip.ToBytes(address);
                query = query.Where(a => a.Address == bytes);
            }
            if (!string.IsNullOrWhiteSpace(hostname))
            {
                string text = hostname.Trim().ToLowerInvariant();
                query = query.Where(a => a.Hostname != null && a.Hostname.ToLower().Contains(text));
            }
            return Results.Ok(await ListAddressesAsync(db, query, 1000));
        });

        api.MapGet("/addresses/{id:int}", async (HttpContext http, AppDbContext db, int id) =>
        {
            IQueryable<Subnet> readable = Context(http).Access.Readable(db.Subnets);
            List<AddressDto> found = await ListAddressesAsync(db, Addresses(db).Where(a => a.Id == id && readable.Any(s => s.Id == a.SubnetId)), 1);
            return found.Count == 0 ? AddressNotFound(id) : Results.Ok(found[0]);
        });

        // Recherche : adresse IP (sous-réseaux qui la contiennent, adresse exacte), réseau CIDR (sous-réseaux qui le recouvrent)
        // ou texte (descriptions, noms d'hôte, propriétaires, MAC, équipements).
        api.MapGet("/search", async (HttpContext http, AppDbContext db, string? q) =>
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return Results.BadRequest(new { error = "Paramètre « q » requis." });
            }
            const int limit = 100;
            SectionAccess access = Context(http).Access;
            string query = q.Trim();
            string text = query.ToLowerInvariant();
            IQueryable<Subnet> readable = access.Readable(db.Subnets);
            List<Subnet> subnets;
            IQueryable<IpAddress> addresses = Addresses(db).Where(a => readable.Any(s => s.Id == a.SubnetId));
            if (IPAddress.TryParse(query, out IPAddress? address))
            {
                IPNetwork host = new(address, address.GetAddressBytes().Length * 8);
                subnets = (await access.Readable(Subnets(db)).ToListAsync()).Where(s => Ip.Contains(s.Network, host)).ToList();
                byte[] bytes = Ip.ToBytes(address);
                addresses = addresses.Where(a => a.Address == bytes);
            }
            else if (query.Contains('/') && Ip.TryParseNetwork(query, out IPNetwork network))
            {
                subnets = (await access.Readable(Subnets(db)).ToListAsync())
                    .Where(s => Ip.Contains(s.Network, network) || Ip.Contains(network, s.Network)).ToList();
                addresses = addresses.Where(a => false);
            }
            else
            {
                subnets = await access.Readable(Subnets(db)).Where(s => s.Description != null && s.Description.ToLower().Contains(text)).ToListAsync();
                addresses = addresses.Where(a => (a.Hostname != null && a.Hostname.ToLower().Contains(text))
                    || (a.Description != null && a.Description.ToLower().Contains(text))
                    || (a.Owner != null && a.Owner.ToLower().Contains(text))
                    || (a.MacAddress != null && a.MacAddress.Contains(text)));
            }
            List<DeviceDto> devices = await DevicesQuery(db, access)
                .Where(d => d.Hostname.ToLower().Contains(text) || (d.IpAddress != null && d.IpAddress.Contains(text))
                    || (d.Description != null && d.Description.ToLower().Contains(text)))
                .OrderBy(d => d.Hostname).Take(limit).Select(DeviceProjection).ToListAsync();
            return Results.Ok(new SearchDto(
                subnets.OrderBy(s => s.SectionId).ThenBy(s => s.Address, ByteComparer).ThenBy(s => s.PrefixLength).Take(limit).Select(s => ToDto(s)).ToList(),
                await ListAddressesAsync(db, addresses, limit),
                devices));
        });

        api.MapGet("/vlans", async (AppDbContext db) =>
        {
            Dictionary<int, Dictionary<string, string>> custom = await CustomFieldsByIdAsync(db, nameof(Vlan));
            return (await db.Vlans.OrderBy(v => v.Number).Select(v => new VlanDto(v.Id, v.Number, v.Name, v.Description, null)).ToListAsync())
                .Select(v => v with { CustomFields = custom.GetValueOrDefault(v.Id) ?? [] });
        });

        api.MapGet("/vrfs", async (AppDbContext db) =>
        {
            Dictionary<int, Dictionary<string, string>> custom = await CustomFieldsByIdAsync(db, nameof(Vrf));
            return (await db.Vrfs.OrderBy(v => v.Name).Select(v => new VrfDto(v.Id, v.Name, v.RouteDistinguisher, v.Description, null)).ToListAsync())
                .Select(v => v with { CustomFields = custom.GetValueOrDefault(v.Id) ?? [] });
        });

        api.MapGet("/devices", async (HttpContext http, AppDbContext db) =>
        {
            Dictionary<int, Dictionary<string, string>> custom = await CustomFieldsByIdAsync(db, nameof(Device));
            return (await DevicesQuery(db, Context(http).Access).OrderBy(d => d.Hostname).Select(DeviceProjection).ToListAsync())
                .Select(d => d with { CustomFields = custom.GetValueOrDefault(d.Id) ?? [] });
        });

    }

    private static void MapSubnetWrites(RouteGroupBuilder api)
    {
        api.MapPost("/subnets", async (HttpContext http, AppDbContext db, SubnetInput input) =>
        {
            ApiContext context = Context(http);
            if (WriteDenied(context) is { } denied)
            {
                return denied;
            }
            if (input.SectionId is not int sectionId || !await db.Sections.AnyAsync(s => s.Id == sectionId))
            {
                return Invalid("sectionId", "Section requise et existante.");
            }
            if (!context.Access.CanWrite(sectionId))
            {
                return Forbidden("Pas de droit d'écriture sur cette section.");
            }
            if (!Ip.TryParseNetwork(input.Network?.Trim() ?? "", out IPNetwork network))
            {
                return Invalid("network", "Réseau CIDR invalide (ex. 10.0.0.0/24, sans bits d'hôte).");
            }
            Subnet subnet = new() { SectionId = sectionId };
            subnet.SetNetwork(network);
            if (await db.Subnets.AnyAsync(s => s.SectionId == sectionId && s.Address == subnet.Address && s.PrefixLength == subnet.PrefixLength))
            {
                return Results.Conflict(new { error = $"{network} existe déjà dans cette section." });
            }
            if (await ApplyAsync(db, subnet, input) is { } invalid)
            {
                return invalid;
            }
            (Dictionary<int, string?> custom, Dictionary<string, string[]> customErrors) = await PrepareCustomFieldsAsync(db, nameof(Subnet), input.CustomFields, creating: true);
            if (customErrors.Count > 0)
            {
                return Results.ValidationProblem(customErrors);
            }
            db.Subnets.Add(subnet);
            await db.SaveChangesAsync();
            await SaveCustomFieldsAsync(db, subnet.Id, custom);
            return Results.Created($"/api/subnets/{subnet.Id}", ToDto(await Subnets(db).SingleAsync(s => s.Id == subnet.Id), await CustomOfAsync(db, nameof(Subnet), subnet.Id)));
        });

        // Le réseau d'un sous-réseau existant ne change pas par l'API (adresses à revalider) : depuis l'interface.
        api.MapPatch("/subnets/{id:int}", async (HttpContext http, AppDbContext db, int id, SubnetInput input) =>
        {
            ApiContext context = Context(http);
            if (WriteDenied(context) is { } denied)
            {
                return denied;
            }
            Subnet? subnet = await context.Access.Readable(db.Subnets).SingleOrDefaultAsync(s => s.Id == id);
            if (subnet is null)
            {
                return SubnetNotFound(id);
            }
            if (!context.Access.CanWrite(subnet.SectionId))
            {
                return Forbidden("Pas de droit d'écriture sur cette section.");
            }
            if (input.Network is not null && input.Network.Trim() != subnet.Network.ToString())
            {
                return Invalid("network", "Le réseau d'un sous-réseau ne se modifie pas par l'API.");
            }
            if (input.SectionId is not null && input.SectionId != subnet.SectionId)
            {
                return Invalid("sectionId", "La section d'un sous-réseau ne se modifie pas par l'API.");
            }
            if (await ApplyAsync(db, subnet, input) is { } invalid)
            {
                return invalid;
            }
            (Dictionary<int, string?> custom, Dictionary<string, string[]> customErrors) = await PrepareCustomFieldsAsync(db, nameof(Subnet), input.CustomFields, creating: false);
            if (customErrors.Count > 0)
            {
                return Results.ValidationProblem(customErrors);
            }
            await db.SaveChangesAsync();
            await SaveCustomFieldsAsync(db, subnet.Id, custom);
            return Results.Ok(ToDto(await Subnets(db).SingleAsync(s => s.Id == id), await CustomOfAsync(db, nameof(Subnet), id)));
        });

        api.MapDelete("/subnets/{id:int}", async (HttpContext http, AppDbContext db, int id) =>
        {
            ApiContext context = Context(http);
            if (WriteDenied(context) is { } denied)
            {
                return denied;
            }
            Subnet? subnet = await context.Access.Readable(db.Subnets).SingleOrDefaultAsync(s => s.Id == id);
            if (subnet is null)
            {
                return SubnetNotFound(id);
            }
            if (!context.Access.CanWrite(subnet.SectionId))
            {
                return Forbidden("Pas de droit d'écriture sur cette section.");
            }
            // Comme la page : les adresses partent en cascade côté base, leurs champs personnalisés d'abord.
            await db.CustomFieldValues
                .Where(v => v.Field!.EntityType == nameof(IpAddress) && db.IpAddresses.Any(a => a.Id == v.EntityId && a.SubnetId == id))
                .ExecuteDeleteAsync();
            db.Subnets.Remove(subnet);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    private static void MapAddressWrites(RouteGroupBuilder api)
    {
        // Sans « address », la première adresse libre est attribuée.
        api.MapPost("/addresses", async (HttpContext http, AppDbContext db, AddressInput input) =>
        {
            ApiContext context = Context(http);
            if (WriteDenied(context) is { } denied)
            {
                return denied;
            }
            Subnet? subnet = input.SubnetId is int subnetId ? await context.Access.Readable(db.Subnets).SingleOrDefaultAsync(s => s.Id == subnetId) : null;
            if (subnet is null)
            {
                return Invalid("subnetId", "Sous-réseau requis et existant.");
            }
            if (!context.Access.CanWrite(subnet.SectionId))
            {
                return Forbidden("Pas de droit d'écriture sur cette section.");
            }
            IpAddress entry = new()
            {
                SubnetId = subnet.Id,
                TagId = await db.Tags.Where(t => t.SystemKey == Tag.UsedKey).Select(t => (int?)t.Id).SingleOrDefaultAsync(),
            };
            if (string.IsNullOrWhiteSpace(input.Address))
            {
                IPAddress? free = await FirstFreeAsync(db, subnet);
                if (free is null)
                {
                    return Results.Conflict(new { error = $"Aucune adresse libre dans {subnet.Network}." });
                }
                entry.Address = Ip.ToBytes(free);
            }
            if (await ApplyAsync(db, subnet, entry, input) is { } invalid)
            {
                return invalid;
            }
            (Dictionary<int, string?> custom, Dictionary<string, string[]> customErrors) = await PrepareCustomFieldsAsync(db, nameof(IpAddress), input.CustomFields, creating: true);
            if (customErrors.Count > 0)
            {
                return Results.ValidationProblem(customErrors);
            }
            db.IpAddresses.Add(entry);
            await db.SaveChangesAsync();
            await SaveCustomFieldsAsync(db, entry.Id, custom);
            return Results.Created($"/api/addresses/{entry.Id}", (await ListAddressesAsync(db, Addresses(db).Where(a => a.Id == entry.Id), 1))[0]);
        });

        api.MapPatch("/addresses/{id:int}", async (HttpContext http, AppDbContext db, int id, AddressInput input) =>
        {
            ApiContext context = Context(http);
            if (WriteDenied(context) is { } denied)
            {
                return denied;
            }
            (IpAddress? entry, Subnet? subnet) = await FindAddressAsync(db, context, id);
            if (entry is null || subnet is null)
            {
                return AddressNotFound(id);
            }
            if (!context.Access.CanWrite(subnet.SectionId))
            {
                return Forbidden("Pas de droit d'écriture sur cette section.");
            }
            if (input.SubnetId is not null && input.SubnetId != entry.SubnetId)
            {
                return Invalid("subnetId", "Une adresse ne change pas de sous-réseau : supprimez-la et recréez-la.");
            }
            if (await ApplyAsync(db, subnet, entry, input) is { } invalid)
            {
                return invalid;
            }
            (Dictionary<int, string?> custom, Dictionary<string, string[]> customErrors) = await PrepareCustomFieldsAsync(db, nameof(IpAddress), input.CustomFields, creating: false);
            if (customErrors.Count > 0)
            {
                return Results.ValidationProblem(customErrors);
            }
            await db.SaveChangesAsync();
            await SaveCustomFieldsAsync(db, entry.Id, custom);
            return Results.Ok((await ListAddressesAsync(db, Addresses(db).Where(a => a.Id == id), 1))[0]);
        });

        api.MapDelete("/addresses/{id:int}", async (HttpContext http, AppDbContext db, int id) =>
        {
            ApiContext context = Context(http);
            if (WriteDenied(context) is { } denied)
            {
                return denied;
            }
            (IpAddress? entry, Subnet? subnet) = await FindAddressAsync(db, context, id);
            if (entry is null || subnet is null)
            {
                return AddressNotFound(id);
            }
            if (!context.Access.CanWrite(subnet.SectionId))
            {
                return Forbidden("Pas de droit d'écriture sur cette section.");
            }
            db.IpAddresses.Remove(entry);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    /// <summary>VLAN, VRF et équipements : réservés aux admins dans l'interface, donc aux clés aux droits d'admin.</summary>
    private static void MapAdminWrites(RouteGroupBuilder api)
    {
        api.MapPost("/vlans", async (HttpContext http, AppDbContext db, VlanInput input) =>
        {
            if (AdminWriteDenied(Context(http)) is { } denied)
            {
                return denied;
            }
            Vlan vlan = new();
            if (await ApplyAsync(db, vlan, input, creating: true) is { } invalid)
            {
                return invalid;
            }
            (Dictionary<int, string?> custom, Dictionary<string, string[]> customErrors) = await PrepareCustomFieldsAsync(db, nameof(Vlan), input.CustomFields, creating: true);
            if (customErrors.Count > 0)
            {
                return Results.ValidationProblem(customErrors);
            }
            db.Vlans.Add(vlan);
            await db.SaveChangesAsync();
            await SaveCustomFieldsAsync(db, vlan.Id, custom);
            return Results.Created($"/api/vlans/{vlan.Id}", new VlanDto(vlan.Id, vlan.Number, vlan.Name, vlan.Description, await CustomOfAsync(db, nameof(Vlan), vlan.Id)));
        });

        api.MapPatch("/vlans/{id:int}", async (HttpContext http, AppDbContext db, int id, VlanInput input) =>
        {
            if (AdminWriteDenied(Context(http)) is { } denied)
            {
                return denied;
            }
            Vlan? vlan = await db.Vlans.FindAsync(id);
            if (vlan is null)
            {
                return NotFound("VLAN", id);
            }
            if (await ApplyAsync(db, vlan, input, creating: false) is { } invalid)
            {
                return invalid;
            }
            (Dictionary<int, string?> custom, Dictionary<string, string[]> customErrors) = await PrepareCustomFieldsAsync(db, nameof(Vlan), input.CustomFields, creating: false);
            if (customErrors.Count > 0)
            {
                return Results.ValidationProblem(customErrors);
            }
            await db.SaveChangesAsync();
            await SaveCustomFieldsAsync(db, vlan.Id, custom);
            return Results.Ok(new VlanDto(vlan.Id, vlan.Number, vlan.Name, vlan.Description, await CustomOfAsync(db, nameof(Vlan), vlan.Id)));
        });

        api.MapDelete("/vlans/{id:int}", async (HttpContext http, AppDbContext db, int id) =>
        {
            if (AdminWriteDenied(Context(http)) is { } denied)
            {
                return denied;
            }
            Vlan? vlan = await db.Vlans.FindAsync(id);
            if (vlan is null)
            {
                return NotFound("VLAN", id);
            }
            db.Vlans.Remove(vlan);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapPost("/vrfs", async (HttpContext http, AppDbContext db, VrfInput input) =>
        {
            if (AdminWriteDenied(Context(http)) is { } denied)
            {
                return denied;
            }
            Vrf vrf = new();
            if (await ApplyAsync(db, vrf, input, creating: true) is { } invalid)
            {
                return invalid;
            }
            (Dictionary<int, string?> custom, Dictionary<string, string[]> customErrors) = await PrepareCustomFieldsAsync(db, nameof(Vrf), input.CustomFields, creating: true);
            if (customErrors.Count > 0)
            {
                return Results.ValidationProblem(customErrors);
            }
            db.Vrfs.Add(vrf);
            await db.SaveChangesAsync();
            await SaveCustomFieldsAsync(db, vrf.Id, custom);
            return Results.Created($"/api/vrfs/{vrf.Id}", new VrfDto(vrf.Id, vrf.Name, vrf.RouteDistinguisher, vrf.Description, await CustomOfAsync(db, nameof(Vrf), vrf.Id)));
        });

        api.MapPatch("/vrfs/{id:int}", async (HttpContext http, AppDbContext db, int id, VrfInput input) =>
        {
            if (AdminWriteDenied(Context(http)) is { } denied)
            {
                return denied;
            }
            Vrf? vrf = await db.Vrfs.FindAsync(id);
            if (vrf is null)
            {
                return NotFound("VRF", id);
            }
            if (await ApplyAsync(db, vrf, input, creating: false) is { } invalid)
            {
                return invalid;
            }
            (Dictionary<int, string?> custom, Dictionary<string, string[]> customErrors) = await PrepareCustomFieldsAsync(db, nameof(Vrf), input.CustomFields, creating: false);
            if (customErrors.Count > 0)
            {
                return Results.ValidationProblem(customErrors);
            }
            await db.SaveChangesAsync();
            await SaveCustomFieldsAsync(db, vrf.Id, custom);
            return Results.Ok(new VrfDto(vrf.Id, vrf.Name, vrf.RouteDistinguisher, vrf.Description, await CustomOfAsync(db, nameof(Vrf), vrf.Id)));
        });

        api.MapDelete("/vrfs/{id:int}", async (HttpContext http, AppDbContext db, int id) =>
        {
            if (AdminWriteDenied(Context(http)) is { } denied)
            {
                return denied;
            }
            Vrf? vrf = await db.Vrfs.FindAsync(id);
            if (vrf is null)
            {
                return NotFound("VRF", id);
            }
            db.Vrfs.Remove(vrf);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapPost("/devices", async (HttpContext http, AppDbContext db, DeviceInput input) =>
        {
            if (AdminWriteDenied(Context(http)) is { } denied)
            {
                return denied;
            }
            Device device = new();
            if (await ApplyAsync(db, device, input, creating: true) is { } invalid)
            {
                return invalid;
            }
            (Dictionary<int, string?> custom, Dictionary<string, string[]> customErrors) = await PrepareCustomFieldsAsync(db, nameof(Device), input.CustomFields, creating: true);
            if (customErrors.Count > 0)
            {
                return Results.ValidationProblem(customErrors);
            }
            db.Devices.Add(device);
            await db.SaveChangesAsync();
            await SaveCustomFieldsAsync(db, device.Id, custom);
            return Results.Created($"/api/devices/{device.Id}", (await DevicesQuery(db, Context(http).Access).Where(d => d.Id == device.Id).Select(DeviceProjection).SingleAsync()) with { CustomFields = await CustomOfAsync(db, nameof(Device), device.Id) });
        });

        api.MapPatch("/devices/{id:int}", async (HttpContext http, AppDbContext db, int id, DeviceInput input) =>
        {
            if (AdminWriteDenied(Context(http)) is { } denied)
            {
                return denied;
            }
            Device? device = await db.Devices.Include(d => d.Sections).SingleOrDefaultAsync(d => d.Id == id);
            if (device is null)
            {
                return NotFound("Équipement", id);
            }
            if (await ApplyAsync(db, device, input, creating: false) is { } invalid)
            {
                return invalid;
            }
            (Dictionary<int, string?> custom, Dictionary<string, string[]> customErrors) = await PrepareCustomFieldsAsync(db, nameof(Device), input.CustomFields, creating: false);
            if (customErrors.Count > 0)
            {
                return Results.ValidationProblem(customErrors);
            }
            await db.SaveChangesAsync();
            await SaveCustomFieldsAsync(db, device.Id, custom);
            return Results.Ok((await DevicesQuery(db, Context(http).Access).Where(d => d.Id == id).Select(DeviceProjection).SingleAsync()) with { CustomFields = await CustomOfAsync(db, nameof(Device), id) });
        });

        api.MapDelete("/devices/{id:int}", async (HttpContext http, AppDbContext db, int id) =>
        {
            if (AdminWriteDenied(Context(http)) is { } denied)
            {
                return denied;
            }
            Device? device = await db.Devices.FindAsync(id);
            if (device is null)
            {
                return NotFound("Équipement", id);
            }
            // Comme la page : références (adresses, RTC) vidées dans la même transaction.
            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync();
            await db.DetachDeviceAsync(id);
            db.Devices.Remove(device);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return Results.NoContent();
        });
    }

    private static async Task<IResult?> ApplyAsync(AppDbContext db, Subnet subnet, SubnetInput input)
    {
        if (input.Description is not null)
        {
            subnet.Description = Clean(input.Description);
        }
        if (input.VlanId is int vlanId)
        {
            subnet.VlanId = vlanId == 0 ? null : await db.Vlans.AnyAsync(v => v.Id == vlanId) ? vlanId : -1;
        }
        if (input.VrfId is int vrfId)
        {
            subnet.VrfId = vrfId == 0 ? null : await db.Vrfs.AnyAsync(v => v.Id == vrfId) ? vrfId : -1;
        }
        if (input.LocationId is int locationId)
        {
            subnet.LocationId = locationId == 0 ? null : await db.Locations.AnyAsync(l => l.Id == locationId) ? locationId : -1;
        }
        if (input.CustomerId is int customerId)
        {
            subnet.CustomerId = customerId == 0 ? null : await db.Customers.AnyAsync(c => c.Id == customerId) ? customerId : -1;
        }
        subnet.PingCheck = input.PingCheck ?? subnet.PingCheck;
        subnet.Discover = input.Discover ?? subnet.Discover;
        subnet.AllowRequests = input.AllowRequests ?? subnet.AllowRequests;
        Dictionary<string, string[]> errors = Validate(subnet);
        // -1 : référence demandée mais inexistante.
        AddIf(errors, subnet.VlanId == -1, "vlanId", "VLAN inexistant.");
        AddIf(errors, subnet.VrfId == -1, "vrfId", "VRF inexistante.");
        AddIf(errors, subnet.LocationId == -1, "locationId", "Emplacement inexistant.");
        AddIf(errors, subnet.CustomerId == -1, "customerId", "Client inexistant.");
        return errors.Count == 0 ? null : Results.ValidationProblem(errors);
    }

    private static async Task<IResult?> ApplyAsync(AppDbContext db, Subnet subnet, IpAddress entry, AddressInput input)
    {
        Dictionary<string, string[]> errors = [];
        if (!string.IsNullOrWhiteSpace(input.Address))
        {
            IPNetwork network = subnet.Network;
            if (!IPAddress.TryParse(input.Address.Trim(), out IPAddress? address) || !Ip.Contains(network, new IPNetwork(address, address.GetAddressBytes().Length * 8)))
            {
                return Invalid("address", $"Adresse invalide ou hors de {network}.");
            }
            (BigInteger first, BigInteger last) = Ip.UsableRange(network);
            BigInteger value = Ip.ToNumber(address);
            if (subnet.IsIPv4 && (value < first || value > last))
            {
                return Invalid("address", "L'adresse réseau et l'adresse de diffusion ne sont pas attribuables.");
            }
            entry.Address = Ip.ToBytes(address);
        }
        if (await db.IpAddresses.AnyAsync(a => a.SubnetId == entry.SubnetId && a.Address == entry.Address && a.Id != entry.Id))
        {
            return Results.Conflict(new { error = $"{Ip.FromBytes(entry.Address)} existe déjà dans le sous-réseau." });
        }
        if (input.Hostname is not null)
        {
            entry.Hostname = Clean(input.Hostname);
        }
        if (input.Description is not null)
        {
            entry.Description = Clean(input.Description);
        }
        if (input.Owner is not null)
        {
            entry.Owner = Clean(input.Owner);
        }
        if (input.MacAddress is not null)
        {
            string? mac = Clean(input.MacAddress);
            entry.MacAddress = mac is null ? null : IpAddress.NormalizeMac(mac);
            AddIf(errors, mac is not null && entry.MacAddress is null, "macAddress", "Adresse MAC invalide (ex. 00:11:22:aa:bb:cc).");
        }
        if (input.Tag is not null)
        {
            // Étiquette par identifiant ou par nom (sans casse) ; vide = aucune.
            string tagText = input.Tag.Trim();
            List<Tag> tags = await db.Tags.ToListAsync();
            Tag? tag = tags.FirstOrDefault(t => t.Id.ToString() == tagText) ?? tags.FirstOrDefault(t => string.Equals(t.Name, tagText, StringComparison.OrdinalIgnoreCase));
            AddIf(errors, tagText.Length > 0 && tag is null, "tag", $"Étiquette « {tagText} » inconnue.");
            entry.TagId = tag?.Id;
        }
        if (input.DeviceId is int deviceId)
        {
            AddIf(errors, deviceId != 0 && !await db.Devices.AnyAsync(d => d.Id == deviceId), "deviceId", "Équipement inexistant.");
            entry.DeviceId = deviceId == 0 ? null : deviceId;
        }
        entry.ExcludePing = input.ExcludePing ?? entry.ExcludePing;
        foreach (KeyValuePair<string, string[]> error in Validate(entry))
        {
            errors.TryAdd(error.Key, error.Value);
        }
        return errors.Count == 0 ? null : Results.ValidationProblem(errors);
    }

    private static async Task<IResult?> ApplyAsync(AppDbContext db, Vlan vlan, VlanInput input, bool creating)
    {
        Dictionary<string, string[]> errors = [];
        AddIf(errors, creating && (input.Number is null || string.IsNullOrWhiteSpace(input.Name)), "number", "Numéro et nom requis.");
        vlan.Number = input.Number ?? vlan.Number;
        vlan.Name = input.Name is null ? vlan.Name : input.Name.Trim();
        vlan.Description = input.Description is null ? vlan.Description : Clean(input.Description);
        AddIf(errors, await db.Vlans.AnyAsync(v => v.Number == vlan.Number && v.Id != vlan.Id), "number", "Ce numéro de VLAN existe déjà.");
        return Merge(errors, Validate(vlan));
    }

    private static async Task<IResult?> ApplyAsync(AppDbContext db, Vrf vrf, VrfInput input, bool creating)
    {
        Dictionary<string, string[]> errors = [];
        AddIf(errors, creating && string.IsNullOrWhiteSpace(input.Name), "name", "Nom requis.");
        vrf.Name = input.Name is null ? vrf.Name : input.Name.Trim();
        vrf.RouteDistinguisher = input.RouteDistinguisher is null ? vrf.RouteDistinguisher : Clean(input.RouteDistinguisher);
        vrf.Description = input.Description is null ? vrf.Description : Clean(input.Description);
        AddIf(errors, await db.Vrfs.AnyAsync(v => v.Name == vrf.Name && v.Id != vrf.Id), "name", "Une VRF porte déjà ce nom.");
        return Merge(errors, Validate(vrf));
    }

    private static async Task<IResult?> ApplyAsync(AppDbContext db, Device device, DeviceInput input, bool creating)
    {
        Dictionary<string, string[]> errors = [];
        AddIf(errors, creating && string.IsNullOrWhiteSpace(input.Hostname), "hostname", "Nom d'hôte requis.");
        device.Hostname = input.Hostname is null ? device.Hostname : input.Hostname.Trim();
        device.Description = input.Description is null ? device.Description : Clean(input.Description);
        if (input.IpAddress is not null)
        {
            string? text = Clean(input.IpAddress);
            device.IpAddress = text is not null && IPAddress.TryParse(text, out IPAddress? ip) ? ip.ToString() : null;
            AddIf(errors, text is not null && device.IpAddress is null, "ipAddress", "Adresse IP invalide.");
        }
        if (input.DeviceTypeId is int typeId)
        {
            AddIf(errors, typeId != 0 && !await db.DeviceTypes.AnyAsync(t => t.Id == typeId), "deviceTypeId", "Type inexistant.");
            device.DeviceTypeId = typeId == 0 ? null : typeId;
        }
        if (input.LocationId is int locationId)
        {
            AddIf(errors, locationId != 0 && !await db.Locations.AnyAsync(l => l.Id == locationId), "locationId", "Emplacement inexistant.");
            device.LocationId = locationId == 0 ? null : locationId;
        }
        if (input.CustomerId is int customerId)
        {
            AddIf(errors, customerId != 0 && !await db.Customers.AnyAsync(c => c.Id == customerId), "customerId", "Client inexistant.");
            device.CustomerId = customerId == 0 ? null : customerId;
        }
        if (input.SectionIds is not null)
        {
            List<Section> sections = await db.Sections.Where(s => input.SectionIds.Contains(s.Id)).ToListAsync();
            AddIf(errors, sections.Count != input.SectionIds.Distinct().Count(), "sectionIds", "Section inexistante.");
            device.Sections.Clear();
            device.Sections.AddRange(sections);
        }
        return Merge(errors, Validate(device));
    }

    /// <summary>Attributs de validation de l'entité (requis, longueurs, plages), avec leurs messages en français.</summary>
    private static Dictionary<string, string[]> Validate(object entity)
    {
        List<ValidationResult> results = [];
        Validator.TryValidateObject(entity, new ValidationContext(entity), results, validateAllProperties: true);
        return results
            .SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : [""]).Select(m => (Member: JsonName(m), Message: r.ErrorMessage ?? "Valeur invalide.")))
            .GroupBy(e => e.Member).ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray());
    }

    private static IResult? Merge(Dictionary<string, string[]> errors, Dictionary<string, string[]> more)
    {
        foreach (KeyValuePair<string, string[]> error in more)
        {
            errors.TryAdd(error.Key, error.Value);
        }
        return errors.Count == 0 ? null : Results.ValidationProblem(errors);
    }

    private static void AddIf(Dictionary<string, string[]> errors, bool condition, string field, string message)
    {
        if (condition)
        {
            errors.TryAdd(field, [message]);
        }
    }

    private static string JsonName(string member) => member.Length == 0 ? "" : char.ToLowerInvariant(member[0]) + member[1..];

    private static string? Clean(string value) => value.Trim() is { Length: > 0 } text ? text : null;

    private static async Task<IPAddress?> FirstFreeAsync(AppDbContext db, Subnet subnet)
    {
        HashSet<BigInteger> used = (await db.IpAddresses.Where(a => a.SubnetId == subnet.Id).Select(a => a.Address).ToListAsync())
            .Select(bytes => Ip.ToNumber(Ip.FromBytes(bytes))).ToHashSet();
        return Ip.FirstFree(subnet.Network, used);
    }

    private static async Task<(IpAddress? Entry, Subnet? Subnet)> FindAddressAsync(AppDbContext db, ApiContext context, int id)
    {
        IpAddress? entry = await db.IpAddresses.FindAsync(id);
        Subnet? subnet = entry is null ? null : await context.Access.Readable(db.Subnets).SingleOrDefaultAsync(s => s.Id == entry.SubnetId);
        return subnet is null ? (null, null) : (entry, subnet);
    }

    private static IResult? WriteDenied(ApiContext context) =>
        context.Key.CanWrite ? null : Forbidden("Clé en lecture seule (Administration › API).");

    private static IResult? AdminWriteDenied(ApiContext context) =>
        WriteDenied(context) ?? (context.Access.IsAdmin ? null : Forbidden("Réservé aux clés ayant les droits d'administration."));

    private static IResult Forbidden(string message) => Results.Json(new { error = message }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult Invalid(string field, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static IResult SubnetNotFound(int id) => NotFound("Sous-réseau", id);

    private static IResult AddressNotFound(int id) => NotFound("Adresse", id);

    private static IResult NotFound(string label, int id) => Results.NotFound(new { error = $"{label} {id} introuvable." });

    private static ApiContext Context(HttpContext http) => (ApiContext)http.Items[ContextItem]!;

    private static readonly Comparer<byte[]> ByteComparer = Comparer<byte[]>.Create((a, b) => ((ReadOnlySpan<byte>)a).SequenceCompareTo(b));

    private static IQueryable<Subnet> Subnets(AppDbContext db) =>
        db.Subnets.Include(s => s.Section).Include(s => s.Vlan).Include(s => s.Vrf).Include(s => s.Location).Include(s => s.Customer);

    private static IQueryable<IpAddress> Addresses(AppDbContext db) =>
        db.IpAddresses.Include(a => a.Subnet).Include(a => a.Tag).Include(a => a.Device);

    private static IQueryable<Device> DevicesQuery(AppDbContext db, SectionAccess access) => access.Readable(db.Devices);

    private static readonly System.Linq.Expressions.Expression<Func<Device, DeviceDto>> DeviceProjection = d =>
        new DeviceDto(d.Id, d.Hostname, d.IpAddress, d.DeviceType!.Name, d.Location!.Name, d.Rack!.Name, d.RackStart, d.RackSize, d.Description,
            d.Sections.Select(s => s.Id).ToArray());

    private static async Task<List<AddressDto>> ListAddressesAsync(AppDbContext db, IQueryable<IpAddress> query, int limit)
    {
        List<IpAddress> addresses = await query.OrderBy(a => a.Address).Take(limit).ToListAsync();
        List<int> ids = addresses.Select(a => a.Id).ToList();
        return await WithCustomFieldsAsync(db, addresses, db.IpAddresses.Where(a => ids.Contains(a.Id)).Select(a => a.Id));
    }

    /// <summary>
    /// Adresses avec leurs champs personnalisés (nom → valeur). <paramref name="ids"/> désigne les mêmes adresses sous forme
    /// de requête : une sous-requête plutôt qu'une liste, qui dépasserait la limite de paramètres de SQL Server sur un grand sous-réseau.
    /// </summary>
    private static async Task<List<AddressDto>> WithCustomFieldsAsync(AppDbContext db, List<IpAddress> addresses, IQueryable<int> ids)
    {
        Dictionary<int, Dictionary<string, string>> custom = (await db.CustomFieldValues.Include(v => v.Field)
                .Where(v => v.Field!.EntityType == nameof(IpAddress) && ids.Contains(v.EntityId)).ToListAsync())
            .GroupBy(v => v.EntityId).ToDictionary(g => g.Key, g => g.ToDictionary(v => v.Field!.Name, v => v.Value));
        return addresses.Select(a => new AddressDto(a.Id, a.SubnetId, a.Subnet?.Network.ToString(), a.Value.ToString(), a.Hostname, a.Description,
            a.MacAddress, a.Owner, a.Tag?.Name, a.Device?.Hostname, a.LastSeen, custom.GetValueOrDefault(a.Id))).ToList();
    }

    private static SubnetDto ToDto(Subnet s, IReadOnlyDictionary<string, string>? custom = null) =>
        new(s.Id, s.SectionId, s.Section?.Name, s.Network.ToString(), s.Description, s.Vlan?.Number, s.Vrf?.Name, s.Location?.Name, s.Customer?.Name, custom);

    /// <summary>
    /// Clé passée en « X-Api-Key » ou « Authorization: Bearer » ; seul son haché est comparé. Calcule les droits de la clé
    /// et attribue les modifications à la clé dans le journal.
    /// </summary>
    private static async ValueTask<object?> RequireApiKeyAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        HttpContext http = context.HttpContext;
        string? key = http.Request.Headers["X-Api-Key"].FirstOrDefault();
        string? authorization = http.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(key) && authorization is not null && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            key = authorization["Bearer ".Length..].Trim();
        }
        AppDbContext? db = http.RequestServices.GetService<AppDbContext>();
        if (string.IsNullOrEmpty(key) || db is null)
        {
            return Results.Json(new { error = "Clé d'API requise (en-tête X-Api-Key)." }, statusCode: StatusCodes.Status401Unauthorized);
        }
        string hash = ApiKey.Hash(key);
        ApiKey? apiKey = await db.ApiKeys.AsNoTracking().SingleOrDefaultAsync(k => k.KeyHash == hash && k.Enabled);
        if (apiKey is null)
        {
            return Results.Json(new { error = "Clé d'API invalide ou désactivée." }, statusCode: StatusCodes.Status401Unauthorized);
        }
        await db.ApiKeys.Where(k => k.Id == apiKey.Id).ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, DateTime.UtcNow));
        http.Items[ContextItem] = new ApiContext(apiKey, await SectionAccess.ForApiKeyAsync(db, apiKey));
        db.AuditUserId = apiKey.UserId;
        db.AuditUserName = $"API « {apiKey.Name} »";
        return await next(context);
    }
}
