using System.Net;
using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Api;

/// <summary>Points d'accès des agents de scan distants, authentifiés par la clé de l'agent (Administration › Agents de scan).</summary>
public static class AgentEndpoints
{
    private const string AgentItem = "ipam.agent";

    public static void MapAgentApi(this WebApplication app)
    {
        RouteGroupBuilder agent = app.MapGroup("/api/agent")
            .AddEndpointFilter(RequireAgentKeyAsync)
            .RequireRateLimiting(ApiEndpoints.RateLimitPolicy)
            .DisableAntiforgery();

        // Sous-réseaux confiés à l'agent dont le dernier scan est plus ancien que l'intervalle.
        agent.MapGet("/work", async (HttpContext http, AppDbContext db, CancellationToken cancellationToken) =>
        {
            RemoteAgent remote = (RemoteAgent)http.Items[AgentItem]!;
            ScanSettings settings = await SettingsStore.LoadAsync<ScanSettings>(db, SettingsStore.ScanPrefix);
            DateTime due = DateTime.UtcNow.AddMinutes(-settings.IntervalMinutes);
            List<Subnet> subnets = await db.Subnets.AsNoTracking()
                .Where(s => s.ScanAgentId == remote.Id && (s.PingCheck || s.Discover) && (s.LastScanAt == null || s.LastScanAt < due))
                .OrderBy(s => s.LastScanAt).ToListAsync(cancellationToken);
            List<AgentTask> tasks = [];
            foreach (Subnet subnet in subnets)
            {
                (List<IPAddress> check, List<IPAddress> discover) = await SubnetScanner.TargetsAsync(db, subnet, cancellationToken);
                tasks.Add(new AgentTask(subnet.Id, subnet.Network.ToString(), check.Select(t => t.ToString()).ToList(), discover.Select(t => t.ToString()).ToList()));
            }
            return new AgentWork(new AgentScanSettings(settings.TimeoutMilliseconds, settings.Parallelism, settings.TcpPortList, settings.ResolveHostnames), tasks);
        });

        agent.MapPost("/results", async (HttpContext http, AppDbContext db, AgentResult result, CancellationToken cancellationToken) =>
        {
            RemoteAgent remote = (RemoteAgent)http.Items[AgentItem]!;
            if (!await db.Subnets.AnyAsync(s => s.Id == result.SubnetId && s.ScanAgentId == remote.Id, cancellationToken))
            {
                return Results.NotFound(new { error = $"Sous-réseau {result.SubnetId} non confié à cet agent." });
            }
            ScanSettings settings = await SettingsStore.LoadAsync<ScanSettings>(db, SettingsStore.ScanPrefix);
            ScanReport report = await SubnetScanner.ApplyAsync(db, result.SubnetId, Parse(result.Checked), Parse(result.Online),
                ParseMap(result.Hostnames), ParseMap(result.Macs), settings, cancellationToken);
            if (report.Discovered > 0 || report.TagChanges > 0)
            {
                await db.LogAsync(LogSeverity.Info, SubnetScanner.LogCategory, $"Agent « {remote.Name} » : {report}.", SubnetScanner.AgentName,
                    http.Connection.RemoteIpAddress?.ToString());
            }
            return Results.Ok(report);
        });
    }

    private static HashSet<IPAddress> Parse(List<string>? addresses) =>
        (addresses ?? []).Select(a => IPAddress.TryParse(a, out IPAddress? address) ? address : null).OfType<IPAddress>().ToHashSet();

    private static Dictionary<IPAddress, string> ParseMap(Dictionary<string, string>? values)
    {
        Dictionary<IPAddress, string> map = [];
        foreach (KeyValuePair<string, string> pair in values ?? [])
        {
            if (IPAddress.TryParse(pair.Key, out IPAddress? address) && !string.IsNullOrWhiteSpace(pair.Value))
            {
                map[address] = pair.Value.Trim();
            }
        }
        return map;
    }

    /// <summary>Clé en « X-Agent-Key » ; seul son haché est comparé. Note le dernier contact de l'agent.</summary>
    private static async ValueTask<object?> RequireAgentKeyAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        HttpContext http = context.HttpContext;
        string? key = http.Request.Headers[AgentProtocol.KeyHeader].FirstOrDefault();
        AppDbContext? db = http.RequestServices.GetService<AppDbContext>();
        if (string.IsNullOrEmpty(key) || db is null)
        {
            return Results.Json(new { error = $"Clé d'agent requise (en-tête {AgentProtocol.KeyHeader})." }, statusCode: StatusCodes.Status401Unauthorized);
        }
        string hash = ApiKey.Hash(key);
        RemoteAgent? remote = await db.RemoteAgents.AsNoTracking().SingleOrDefaultAsync(a => a.KeyHash == hash && a.Enabled);
        if (remote is null)
        {
            return Results.Json(new { error = "Clé d'agent invalide ou agent désactivé." }, statusCode: StatusCodes.Status401Unauthorized);
        }
        string? address = http.Connection.RemoteIpAddress?.ToString();
        string? version = http.Request.Headers[AgentProtocol.VersionHeader].FirstOrDefault() is { } v ? v[..Math.Min(v.Length, 50)] : null;
        await db.RemoteAgents.Where(a => a.Id == remote.Id).ExecuteUpdateAsync(s => s
            .SetProperty(a => a.LastContactAt, DateTime.UtcNow)
            .SetProperty(a => a.LastContactAddress, address)
            .SetProperty(a => a.Version, version));
        http.Items[AgentItem] = remote;
        return await next(context);
    }
}
