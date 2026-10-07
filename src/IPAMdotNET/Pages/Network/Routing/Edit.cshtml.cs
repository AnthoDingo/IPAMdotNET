using System.Net;
using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Network.Routing;

[Authorize(Policy = "Admin")]
public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public BgpPeer Peer { get; set; } = new();

    [BindProperty]
    public LinkedLinesForm Advertised { get; set; } = new();

    [BindProperty]
    public LinkedLinesForm Received { get; set; } = new();

    public List<SelectListItem> Vrfs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is not null)
        {
            BgpPeer? peer = await db.BgpPeers.Include(p => p.Subnets).SingleOrDefaultAsync(p => p.Id == id);
            if (peer is null)
            {
                return NotFound();
            }
            Peer = peer;
            Advertised = await SubnetsFormAsync(peer, BgpDirection.Advertised);
            Received = await SubnetsFormAsync(peer, BgpDirection.Received);
        }
        await LoadVrfsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Peer.Id = id ?? 0;
        Peer.LocalAddress = Normalize(Peer.LocalAddress, "Peer.LocalAddress");
        Peer.PeerAddress = Normalize(Peer.PeerAddress, "Peer.PeerAddress");
        if (id is not null && !await db.BgpPeers.AnyAsync(p => p.Id == id))
        {
            return NotFound();
        }
        List<BgpPeerSubnet> subnets = [
            .. await SubnetsAsync(Advertised, BgpDirection.Advertised, "Advertised.Text"),
            .. await SubnetsAsync(Received, BgpDirection.Received, "Received.Text"),
        ];
        if (!ModelState.IsValid)
        {
            await LoadVrfsAsync();
            return Page();
        }
        BgpPeer peer = id is null ? Peer : await db.BgpPeers.Include(p => p.Subnets).SingleAsync(p => p.Id == id);
        if (id is not null)
        {
            db.Entry(peer).CurrentValues.SetValues(Peer);
            // Associations inchangées conservées (pas de suppression / recréation inutile dans le journal).
            db.BgpPeerSubnets.RemoveRange(peer.Subnets.Where(x => !subnets.Any(n => n.SubnetId == x.SubnetId && n.Direction == x.Direction)).ToList());
            subnets.RemoveAll(n => peer.Subnets.Any(x => x.SubnetId == n.SubnetId && x.Direction == n.Direction));
        }
        else
        {
            db.BgpPeers.Add(peer);
        }
        peer.Subnets.AddRange(subnets);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        BgpPeer? peer = await db.BgpPeers.FindAsync(id);
        if (peer is null)
        {
            return NotFound();
        }
        db.BgpPeers.Remove(peer);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    private string Normalize(string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
        if (IPAddress.TryParse(value.Trim(), out IPAddress? address))
        {
            return address.ToString();
        }
        ModelState.AddModelError(field, "Adresse IP invalide.");
        return value;
    }

    /// <summary>Sous-réseaux d'un sens, une ligne par réseau de l'IPAM (doublons ignorés).</summary>
    private async Task<List<BgpPeerSubnet>> SubnetsAsync(LinkedLinesForm form, BgpDirection direction, string field) =>
        (await LinkedLines.ResolveAsync(db, form, ModelState, field, subnetsOnly: true))
            .Where(s => s.SubnetId is not null).Select(s => s.SubnetId!.Value).Distinct()
            .Select(subnetId => new BgpPeerSubnet { SubnetId = subnetId, Direction = direction }).ToList();

    private async Task<LinkedLinesForm> SubnetsFormAsync(BgpPeer peer, BgpDirection direction)
    {
        List<int> ids = peer.Subnets.Where(x => x.Direction == direction).Select(x => x.SubnetId).ToList();
        List<Subnet> subnets = await db.Subnets.Where(s => ids.Contains(s.Id)).ToListAsync();
        return await LinkedLines.FormAsync(db, subnets.OrderBy(s => s.Address, Comparer<byte[]>.Create((a, b) => a.AsSpan().SequenceCompareTo(b)))
            .ThenBy(s => s.PrefixLength).Select(s => (s.Network.ToString(), (int?)s.Id, (int?)null)));
    }

    private async Task LoadVrfsAsync()
    {
        await LinkedLines.LabelAsync(db, Advertised);
        await LinkedLines.LabelAsync(db, Received);
        Vrfs = await db.Vrfs.OrderBy(v => v.Name)
            .Select(v => new SelectListItem(v.Name, v.Id.ToString()))
            .ToListAsync();
    }
}
