using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Network.Routing;

public class IndexModel(AppDbContext db) : PageModel
{
    public List<BgpPeer> Peers { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Peers = await db.BgpPeers.Include(b => b.Vrf).OrderBy(b => b.Name).ToListAsync();
    }
}
