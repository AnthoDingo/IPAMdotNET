using System.Net;
using IPAMdotNet.Data;
using IPAMdotNet.Navigation;
using IPAMdotNet.Networking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Tools.Requests;

public class NewModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public IpRequest IpRequest { get; set; } = new();

    public List<SelectListItem> Subnets { get; private set; } = [];

    public async Task OnGetAsync(int? subnetId)
    {
        IpRequest.SubnetId = subnetId ?? 0;
        await LoadSubnetsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Subnet? subnet = await db.Subnets.FindAsync(IpRequest.SubnetId);
        if (subnet is null || !subnet.AllowRequests)
        {
            ModelState.AddModelError("IpRequest.SubnetId", "Ce sous-réseau n'accepte pas les demandes.");
        }
        else if (!string.IsNullOrWhiteSpace(IpRequest.RequestedAddress))
        {
            if (IPAddress.TryParse(IpRequest.RequestedAddress.Trim(), out IPAddress? address)
                && Ip.Contains(subnet.Network, new IPNetwork(address, address.GetAddressBytes().Length * 8)))
            {
                IpRequest.RequestedAddress = address.ToString();
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
        IpRequest.RequestedAt = DateTime.UtcNow;
        IpRequest.AssignedAddress = null;
        IpRequest.AdminComment = null;
        IpRequest.ProcessedById = null;
        IpRequest.ProcessedAt = null;
        db.IpRequests.Add(IpRequest);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    private async Task LoadSubnetsAsync()
    {
        List<Subnet> subnets = await db.Subnets.Include(s => s.Section)
            .Where(s => s.AllowRequests)
            .OrderBy(s => s.Section!.Name).ThenBy(s => s.Address).ThenBy(s => s.PrefixLength)
            .ToListAsync();
        Subnets = subnets
            .Select(s => new SelectListItem($"{s.Section?.Name} — {s.Network}{(s.Description is null ? "" : $" ({s.Description})")}", s.Id.ToString()))
            .ToList();
    }
}
