using System.Net;
using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using IPAMdotNet.Navigation;
using IPAMdotNet.Networking;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Tools.Requests;

[IpRequestsEnabled]
public class NewModel(AppDbContext db, IDataProtectionProvider protection) : PageModel
{
    [BindProperty]
    public IpRequest IpRequest { get; set; } = new();

    public List<SelectListItem> Subnets { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? subnetId)
    {
        IpRequest.SubnetId = subnetId ?? 0;
        await LoadSubnetsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        SectionAccess access = await SectionAccess.ForAsync(db, User);
        Subnet? subnet = await db.Subnets.FindAsync(IpRequest.SubnetId);
        if (subnet is null || !subnet.AllowRequests || !access.CanRead(subnet.SectionId))
        {
            ModelState.AddModelError("IpRequest.SubnetId", "Ce sous-réseau n'accepte pas les demandes.");
        }
        else if (!string.IsNullOrWhiteSpace(IpRequest.RequestedAddress))
        {
            if (IPAddress.TryParse(IpRequest.RequestedAddress.Trim(), out IPAddress? address)
                && Ip.Contains(subnet.Network, new IPNetwork(address, address.GetAddressBytes().Length * 8)))
            {
                IpRequest.RequestedAddress = address.ToString();
                byte[] bytes = Ip.ToBytes(address);
                if (await db.IpAddresses.AnyAsync(a => a.SubnetId == subnet.Id && a.Address == bytes))
                {
                    ModelState.AddModelError("IpRequest.RequestedAddress", "Cette adresse est déjà utilisée : laissez le champ vide pour qu'un administrateur en choisisse une.");
                }
            }
            else
            {
                ModelState.AddModelError("IpRequest.RequestedAddress", $"Adresse invalide ou hors de {subnet.Network}.");
            }
        }
        if (!ModelState.IsValid)
        {
            await LoadSubnetsAsync();
            return Page();
        }

        IpRequest.Id = 0;
        IpRequest.State = IpRequestState.Pending;
        IpRequest.RequestedById = User.UserId();
        IpRequest.RequesterEmail = null;
        IpRequest.RequestedAt = DateTime.UtcNow;
        IpRequest.AssignedAddress = null;
        IpRequest.AdminComment = null;
        IpRequest.ProcessedById = null;
        IpRequest.ProcessedAt = null;
        db.IpRequests.Add(IpRequest);
        await db.SaveChangesAsync();
        await NotifyAdminsAsync(db, protection, IpRequest, subnet!, User.Identity?.Name ?? "");
        return RedirectToPage("Index");
    }

    /// <summary>Courriel aux admins (ayant une adresse) : nouvelle demande à traiter.</summary>
    public static async Task NotifyAdminsAsync(AppDbContext db, IDataProtectionProvider protection, IpRequest request, Subnet subnet, string requester)
    {
        List<string> admins = await db.Users.Where(u => u.IsAdmin && u.Enabled && u.Email != null).Select(u => u.Email!).ToListAsync();
        await Mailer.SendAsync(db, protection, admins, "Nouvelle demande d'adresse",
            $"{requester} demande {(request.Count > 1 ? $"{request.Count} adresses" : "une adresse")} dans {subnet.Network}" +
            (request.RequestedAddress is null ? "" : $" (souhaitée : {request.RequestedAddress})") +
            $".\n\nMotif : {request.Description}\n\nTraiter la demande : {Mailer.Link($"/Tools/Requests/Process/{request.Id}")}");
    }

    private async Task LoadSubnetsAsync()
    {
        SectionAccess access = await SectionAccess.ForAsync(db, User);
        List<Subnet> subnets = await access.Readable(db.Subnets).Include(s => s.Section)
            .Where(s => s.AllowRequests)
            .OrderBy(s => s.Section!.Name).ThenBy(s => s.Address).ThenBy(s => s.PrefixLength)
            .ToListAsync();
        Subnets = subnets
            .Select(s => new SelectListItem($"{s.Section?.Name} — {s.Network}{(s.Description is null ? "" : $" ({s.Description})")}", s.Id.ToString()))
            .ToList();
    }
}
