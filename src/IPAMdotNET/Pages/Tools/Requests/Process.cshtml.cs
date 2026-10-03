using System.ComponentModel.DataAnnotations;
using System.Net;
using IPAMdotNet.Data;
using IPAMdotNet.Navigation;
using IPAMdotNet.Networking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Tools.Requests;

[Authorize(Policy = "Admin")]
public class ProcessModel(AppDbContext db) : PageModel
{
    public IpRequest IpRequest { get; private set; } = new();

    [BindProperty, MaxLength(45), Display(Name = "Adresse attribuée")]
    public string? AssignedAddress { get; set; }

    [BindProperty, MaxLength(500), Display(Name = "Commentaire")]
    public string? AdminComment { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }
        AssignedAddress = IpRequest.RequestedAddress;
        return Page();
    }

    public async Task<IActionResult> OnPostApproveAsync(int id)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }
        if (!IPAddress.TryParse(AssignedAddress?.Trim(), out IPAddress? address)
            || !Ip.Contains(IpRequest.Subnet!.Network, new IPNetwork(address, address.GetAddressBytes().Length * 8)))
        {
            ModelState.AddModelError(nameof(AssignedAddress), $"Adresse invalide ou hors de {IpRequest.Subnet!.Network}.");
        }
        else
        {
            AssignedAddress = address.ToString();
            // ponytail: unicité vérifiée sur les seules demandes acceptées, à étendre aux adresses IP quand elles seront gérées.
            if (await db.IpRequests.AnyAsync(r => r.SubnetId == IpRequest.SubnetId && r.Id != id
                && r.State == IpRequestState.Approved && r.AssignedAddress == AssignedAddress))
            {
                ModelState.AddModelError(nameof(AssignedAddress), "Cette adresse a déjà été attribuée par une autre demande.");
            }
        }
        if (!ModelState.IsValid)
        {
            return Page();
        }
        return await CloseAsync(IpRequestState.Approved);
    }

    public async Task<IActionResult> OnPostRejectAsync(int id)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }
        if (string.IsNullOrWhiteSpace(AdminComment))
        {
            ModelState.AddModelError(nameof(AdminComment), "Indiquez le motif du refus.");
            return Page();
        }
        AssignedAddress = null;
        return await CloseAsync(IpRequestState.Rejected);
    }

    private async Task<IActionResult> CloseAsync(IpRequestState state)
    {
        IpRequest.State = state;
        IpRequest.AssignedAddress = AssignedAddress;
        IpRequest.AdminComment = string.IsNullOrWhiteSpace(AdminComment) ? null : AdminComment.Trim();
        IpRequest.ProcessedById = User.UserId();
        IpRequest.ProcessedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    /// <summary>Charge la demande (suivie) ; false si elle n'existe pas ou est déjà traitée.</summary>
    private async Task<bool> LoadAsync(int id)
    {
        IpRequest? request = await db.IpRequests
            .Include(r => r.Subnet).ThenInclude(s => s!.Section)
            .Include(r => r.RequestedBy)
            .SingleOrDefaultAsync(r => r.Id == id && r.State == IpRequestState.Pending);
        if (request is null)
        {
            return false;
        }
        IpRequest = request;
        return true;
    }
}
