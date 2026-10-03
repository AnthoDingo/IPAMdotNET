using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using System.Numerics;
using IPAMdotNet.Navigation;
using IPAMdotNet.Networking;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages;

public class IndexModel(AppDbContext db) : PageModel
{
    public int SectionCount { get; private set; }
    public int SubnetCount { get; private set; }
    public int VlanCount { get; private set; }
    public int VrfCount { get; private set; }
    public int DeviceCount { get; private set; }
    public int UserCount { get; private set; }

    public List<Subnet> Favorites { get; private set; } = [];
    public List<IpRequest> PendingRequests { get; private set; } = [];
    public int PendingRequestCount { get; private set; }
    public List<ChangeLog> LastChanges { get; private set; } = [];
    public WidgetSettings Widgets { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Widgets = await SettingsStore.LoadAsync<WidgetSettings>(db, SettingsStore.WidgetsPrefix);
        SectionCount = await db.Sections.CountAsync();
        SubnetCount = await db.Subnets.CountAsync();
        VlanCount = await db.Vlans.CountAsync();
        VrfCount = await db.Vrfs.CountAsync();
        DeviceCount = await db.Devices.CountAsync();
        UserCount = await db.Users.CountAsync();

        int userId = User.UserId();
        SectionAccess access = await SectionAccess.ForAsync(db, User);
        Favorites = await access.Readable(db.Subnets)
            .Where(s => db.FavoriteSubnets.Any(f => f.UserId == userId && f.SubnetId == s.Id))
            .OrderBy(s => s.Address).ThenBy(s => s.PrefixLength).Take(10).ToListAsync();

        // Un admin voit toutes les demandes en attente, un utilisateur les siennes.
        IQueryable<IpRequest> pending = db.IpRequests.Include(r => r.Subnet).Where(r => r.State == IpRequestState.Pending);
        if (!User.IsInRole("Admin"))
        {
            pending = pending.Where(r => r.RequestedById == userId);
        }
        PendingRequestCount = await pending.CountAsync();
        PendingRequests = await pending.OrderBy(r => r.RequestedAt).Take(5).ToListAsync();

        LastChanges = await db.ChangeLogs.OrderByDescending(c => c.Date).ThenByDescending(c => c.Id).Take(8).ToListAsync();

        // Top 10 : IPv4 par taux d'occupation, IPv6 par nombre d'adresses (un pourcentage n'y a pas de sens).
        AddressCount = await db.IpAddresses.CountAsync();
        Dictionary<int, int> usage = await SubnetTree.UsageAsync(db.IpAddresses);
        List<Subnet> readable = await access.Readable(db.Subnets).ToListAsync();
        List<SubnetNode> used = readable.Where(s => usage.ContainsKey(s.Id)).Select(s => new SubnetNode(s, 0, null, usage[s.Id])).ToList();
        TopIpv4 = used.Where(n => n.Subnet.IsIPv4)
            .OrderByDescending(n => (double)n.Used / (double)BigInteger.Max(1, Ip.UsableCount(n.Subnet.Network))).Take(10).ToList();
        TopIpv6 = used.Where(n => !n.Subnet.IsIPv4).OrderByDescending(n => n.Used).Take(10).ToList();
    }

    public int AddressCount { get; private set; }
    public List<SubnetNode> TopIpv4 { get; private set; } = [];
    public List<SubnetNode> TopIpv6 { get; private set; } = [];
}
