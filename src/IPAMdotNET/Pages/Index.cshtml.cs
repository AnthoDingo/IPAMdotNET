using IPAMdotNet.Data;
using IPAMdotNet.Navigation;
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

    public async Task OnGetAsync()
    {
        SectionCount = await db.Sections.CountAsync();
        SubnetCount = await db.Subnets.CountAsync();
        VlanCount = await db.Vlans.CountAsync();
        VrfCount = await db.Vrfs.CountAsync();
        DeviceCount = await db.Devices.CountAsync();
        UserCount = await db.Users.CountAsync();

        int userId = User.UserId();
        Favorites = await db.FavoriteSubnets.Where(f => f.UserId == userId).Select(f => f.Subnet!)
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
    }
}
