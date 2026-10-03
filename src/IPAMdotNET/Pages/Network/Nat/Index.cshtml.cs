using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Network.Nat;

public class IndexModel(AppDbContext db) : PageModel
{
    public List<NatRule> Rules { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Rules = await db.NatRules.OrderBy(n => n.Name).ToListAsync();
    }
}
