using System.Net;
using IPAMdotNet.Data;
using IPAMdotNet.Networking;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IPAMdotNet.Pages.Network.Multicast;

public class IndexModel(AppDbContext db) : PageModel
{
    private static readonly IPNetwork Ipv4Multicast = IPNetwork.Parse("224.0.0.0/4");
    private static readonly IPNetwork Ipv6Multicast = IPNetwork.Parse("ff00::/8");

    public List<SectionSubnets> Sections { get; private set; } = [];

    public async Task OnGetAsync()
    {
        SectionAccess access = await SectionAccess.ForAsync(db, User);
        Sections = await SubnetTree.LoadAllAsync(db, s => access.CanRead(s.SectionId)
            && (Ip.Contains(Ipv4Multicast, s.Network) || Ip.Contains(Ipv6Multicast, s.Network)));
    }
}
