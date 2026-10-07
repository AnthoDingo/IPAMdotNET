using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Network.Nat;

public class IndexModel(AppDbContext db) : PageModel
{
    public List<NatRule> Rules { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Rules = await db.NatRules.Include(n => n.SourceSubnet).Include(n => n.SourceAddress).Include(n => n.DestinationSubnet)
            .Include(n => n.DestinationAddress).Include(n => n.Device).OrderBy(n => n.Name).ToListAsync();
    }
}
