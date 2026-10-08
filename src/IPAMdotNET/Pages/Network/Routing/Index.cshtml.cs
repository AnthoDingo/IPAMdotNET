using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Network.Routing;

public class IndexModel(AppDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true, Name = CustomFieldList.Prefix)]
    public CustomFieldList Custom { get; set; } = new();

    public List<BgpPeer> Peers { get; private set; } = [];

    /// <summary>Sous-réseaux annoncés / reçus par pair, limités aux sections lisibles.</summary>
    public ILookup<(int PeerId, BgpDirection Direction), Subnet> Subnets { get; private set; } = Array.Empty<Subnet>().ToLookup(_ => (0, BgpDirection.Advertised));

    public async Task OnGetAsync()
    {
        await Custom.LoadAsync(db, nameof(BgpPeer));
        Peers = await Custom.Apply(db, db.BgpPeers).Include(b => b.Vrf).OrderBy(b => b.Name).ToListAsync();
        IQueryable<Subnet> readable = (await SectionAccess.ForAsync(db, User)).Readable(db.Subnets);
        Subnets = (await db.BgpPeerSubnets.Where(x => readable.Any(s => s.Id == x.SubnetId)).Include(x => x.Subnet).ToListAsync())
            .OrderBy(x => x.Subnet!.Address, Comparer<byte[]>.Create((a, b) => a.AsSpan().SequenceCompareTo(b)))
            .ThenBy(x => x.Subnet!.PrefixLength)
            .ToLookup(x => (x.BgpPeerId, x.Direction), x => x.Subnet!);
    }
}
