using System.Net;
using IPAMdotNet.Data;
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

    public List<SelectListItem> Vrfs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is not null)
        {
            BgpPeer? peer = await db.BgpPeers.FindAsync(id);
            if (peer is null)
            {
                return NotFound();
            }
            Peer = peer;
        }
        await LoadVrfsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Peer.Id = id ?? 0;
        Peer.LocalAddress = Normalize(Peer.LocalAddress, "Peer.LocalAddress");
        Peer.PeerAddress = Normalize(Peer.PeerAddress, "Peer.PeerAddress");
        if (!ModelState.IsValid)
        {
            await LoadVrfsAsync();
            return Page();
        }
        db.Update(Peer);
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

    private async Task LoadVrfsAsync()
    {
        Vrfs = await db.Vrfs.OrderBy(v => v.Name)
            .Select(v => new SelectListItem(v.Name, v.Id.ToString()))
            .ToListAsync();
    }
}
