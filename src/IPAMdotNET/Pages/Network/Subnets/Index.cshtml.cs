using IPAMdotNet.Data;
using IPAMdotNet.Networking;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IPAMdotNet.Pages.Network.Subnets;

public class IndexModel(AppDbContext db) : PageModel
{
    public List<SectionSubnets> Sections { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Sections = await SubnetTree.LoadAllAsync(db);
    }
}
