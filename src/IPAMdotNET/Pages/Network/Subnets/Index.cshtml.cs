using IPAMdotNet.Data;
using IPAMdotNet.Networking;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IPAMdotNet.Pages.Network.Subnets;

public class IndexModel(AppDbContext db) : PageModel
{
    public List<SectionSubnets> Sections { get; private set; } = [];
    public bool CanWriteAny { get; private set; }

    public async Task OnGetAsync()
    {
        SectionAccess access = await SectionAccess.ForAsync(db, User);
        CanWriteAny = access.CanWriteAny;
        Sections = await SubnetTree.LoadAllAsync(db, s => access.CanRead(s.SectionId));
    }
}
