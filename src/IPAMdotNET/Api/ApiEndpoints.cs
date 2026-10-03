using System.Net;
using IPAMdotNet.Data;
using IPAMdotNet.Networking;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Api;

public sealed record SectionDto(int Id, string Name, string? Description);
public sealed record SubnetDto(int Id, int SectionId, string? Section, string Network, string? Description, int? Vlan, string? Vrf,
    string? Location, string? Customer, IReadOnlyDictionary<string, string>? CustomFields = null);
public sealed record AddressDto(int Id, int SubnetId, string? Subnet, string Address, string? Hostname, string? Description,
    string? MacAddress, string? Owner, string? Tag, string? Device, DateTime? LastSeen);
public sealed record VlanDto(int Id, int Number, string Name, string? Description);
public sealed record VrfDto(int Id, string Name, string? RouteDistinguisher, string? Description);
public sealed record DeviceDto(int Id, string Hostname, string? IpAddress, string? Type, string? Location, string? Rack, int? RackStart, int? RackSize, string? Description);
public sealed record LocationDto(int Id, string Name, string? Address, string? Latitude, string? Longitude, string? Description);
public sealed record CustomerDto(int Id, string Name, string? City, string? ContactPerson, string? ContactPhone, string? ContactMail);

/// <summary>API REST en lecture seule, authentifiée par clé d'API (Administration › API).</summary>
public static class ApiEndpoints
{
    public const string RateLimitPolicy = "api";

    public static void MapIpamApi(this WebApplication app)
    {
        RouteGroupBuilder api = app.MapGroup("/api")
            .AddEndpointFilter(RequireApiKeyAsync)
            .RequireRateLimiting(RateLimitPolicy)
            .DisableAntiforgery();

        api.MapGet("/sections", async (AppDbContext db) =>
            await db.Sections.OrderBy(s => s.Name).Select(s => new SectionDto(s.Id, s.Name, s.Description)).ToListAsync());

        api.MapGet("/subnets", async (AppDbContext db, int? section, string? ip) =>
        {
            IQueryable<Subnet> query = Subnets(db);
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

        api.MapGet("/subnets/{id:int}", async (AppDbContext db, int id) =>
        {
            Subnet? subnet = await Subnets(db).SingleOrDefaultAsync(s => s.Id == id);
            if (subnet is null)
            {
                return Results.NotFound(new { error = $"Sous-réseau {id} introuvable." });
            }
            Dictionary<string, string> custom = await db.CustomFieldValues
                .Where(v => v.EntityId == id && v.Field!.EntityType == nameof(Subnet))
                .ToDictionaryAsync(v => v.Field!.Name, v => v.Value);
            return Results.Ok(ToDto(subnet, custom));
        });

        api.MapGet("/subnets/{id:int}/addresses", async (AppDbContext db, int id) =>
            await db.Subnets.AnyAsync(s => s.Id == id)
                ? Results.Ok((await Addresses(db).Where(a => a.SubnetId == id).OrderBy(a => a.Address).ToListAsync()).Select(ToDto))
                : Results.NotFound(new { error = $"Sous-réseau {id} introuvable." }));

        api.MapGet("/addresses", async (AppDbContext db, string? ip, string? hostname) =>
        {
            IQueryable<IpAddress> query = Addresses(db);
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
            return Results.Ok((await query.OrderBy(a => a.Address).Take(1000).ToListAsync()).Select(ToDto));
        });

        api.MapGet("/vlans", async (AppDbContext db) =>
            await db.Vlans.OrderBy(v => v.Number).Select(v => new VlanDto(v.Id, v.Number, v.Name, v.Description)).ToListAsync());

        api.MapGet("/vrfs", async (AppDbContext db) =>
            await db.Vrfs.OrderBy(v => v.Name).Select(v => new VrfDto(v.Id, v.Name, v.RouteDistinguisher, v.Description)).ToListAsync());

        api.MapGet("/devices", async (AppDbContext db) =>
            await db.Devices.OrderBy(d => d.Hostname)
                .Select(d => new DeviceDto(d.Id, d.Hostname, d.IpAddress, d.DeviceType!.Name, d.Location!.Name, d.Rack!.Name, d.RackStart, d.RackSize, d.Description))
                .ToListAsync());

        api.MapGet("/locations", async (AppDbContext db) =>
            await db.Locations.OrderBy(l => l.Name).Select(l => new LocationDto(l.Id, l.Name, l.Address, l.Latitude, l.Longitude, l.Description)).ToListAsync());

        api.MapGet("/customers", async (AppDbContext db) =>
            await db.Customers.OrderBy(c => c.Name).Select(c => new CustomerDto(c.Id, c.Name, c.City, c.ContactPerson, c.ContactPhone, c.ContactMail)).ToListAsync());
    }

    private static IQueryable<Subnet> Subnets(AppDbContext db) =>
        db.Subnets.Include(s => s.Section).Include(s => s.Vlan).Include(s => s.Vrf).Include(s => s.Location).Include(s => s.Customer);

    private static IQueryable<IpAddress> Addresses(AppDbContext db) =>
        db.IpAddresses.Include(a => a.Subnet).Include(a => a.Tag).Include(a => a.Device);

    private static AddressDto ToDto(IpAddress a) =>
        new(a.Id, a.SubnetId, a.Subnet?.Network.ToString(), a.Value.ToString(), a.Hostname, a.Description, a.MacAddress, a.Owner,
            a.Tag?.Name, a.Device?.Hostname, a.LastSeen);

    private static SubnetDto ToDto(Subnet s, IReadOnlyDictionary<string, string>? custom = null) =>
        new(s.Id, s.SectionId, s.Section?.Name, s.Network.ToString(), s.Description, s.Vlan?.Number, s.Vrf?.Name, s.Location?.Name, s.Customer?.Name, custom);

    /// <summary>Clé passée en « X-Api-Key » ou « Authorization: Bearer » ; seul son haché est comparé.</summary>
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
        int? keyId = await db.ApiKeys.Where(k => k.KeyHash == hash && k.Enabled).Select(k => (int?)k.Id).SingleOrDefaultAsync();
        if (keyId is null)
        {
            return Results.Json(new { error = "Clé d'API invalide ou désactivée." }, statusCode: StatusCodes.Status401Unauthorized);
        }
        await db.ApiKeys.Where(k => k.Id == keyId).ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, DateTime.UtcNow));
        return await next(context);
    }
}
