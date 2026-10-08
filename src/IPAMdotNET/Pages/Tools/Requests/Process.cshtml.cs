using System.ComponentModel.DataAnnotations;
using System.Net;
using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using IPAMdotNet.Navigation;
using IPAMdotNet.Networking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Tools.Requests;

[IpRequestsEnabled]
[Authorize(Policy = "Admin")]
public class ProcessModel(AppDbContext db, IDataProtectionProvider protection) : PageModel
{
    public IpRequest IpRequest { get; private set; } = new();

    [BindProperty, MaxLength(45), Display(Name = "Adresse attribuée")]
    public string? AssignedAddress { get; set; }

    [BindProperty, MaxLength(500), Display(Name = "Commentaire")]
    public string? AdminComment { get; set; }

    /// <summary>Première adresse attribuable libre du sous-réseau (null si plein).</summary>
    public IPAddress? FirstFree { get; private set; }

    /// <summary>L'adresse demandée est déjà utilisée (attribuée entre-temps) : la première libre est proposée à la place.</summary>
    public bool RequestedTaken { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }
        // Adresse demandée si elle est encore libre, sinon la première libre du sous-réseau (comme phpIPAM).
        HashSet<System.Numerics.BigInteger> used = (await db.IpAddresses.Where(a => a.SubnetId == IpRequest.SubnetId).Select(a => a.Address).ToListAsync())
            .Select(bytes => Ip.ToNumber(Ip.FromBytes(bytes))).ToHashSet();
        FirstFree = Ip.FirstFree(IpRequest.Subnet!.Network, used);
        bool requestedFree = IPAddress.TryParse(IpRequest.RequestedAddress, out IPAddress? requested) && !used.Contains(Ip.ToNumber(requested));
        RequestedTaken = IpRequest.RequestedAddress is not null && !requestedFree;
        AssignedAddress = requestedFree ? IpRequest.RequestedAddress : FirstFree?.ToString();
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
            byte[] bytes = Ip.ToBytes(address);
            (System.Numerics.BigInteger first, System.Numerics.BigInteger last) = Ip.UsableRange(IpRequest.Subnet!.Network);
            System.Numerics.BigInteger value = Ip.ToNumber(address);
            if (IpRequest.Subnet.IsIPv4 && (value < first || value > last))
            {
                ModelState.AddModelError(nameof(AssignedAddress), "L'adresse réseau et l'adresse de diffusion ne sont pas attribuables.");
            }
            else if (await db.IpAddresses.AnyAsync(a => a.SubnetId == IpRequest.SubnetId && a.Address == bytes))
            {
                ModelState.AddModelError(nameof(AssignedAddress), "Cette adresse est déjà utilisée dans le sous-réseau.");
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
        if (state == IpRequestState.Approved && IPAddress.TryParse(AssignedAddress, out IPAddress? address))
        {
            // L'adresse acceptée est créée dans le sous-réseau, avec les informations de la demande.
            db.IpAddresses.Add(new IpAddress
            {
                SubnetId = IpRequest.SubnetId,
                Address = Ip.ToBytes(address),
                Hostname = IpRequest.Hostname,
                Owner = IpRequest.Owner,
                Description = IpRequest.Description.Length > 500 ? IpRequest.Description[..500] : IpRequest.Description,
                TagId = await db.Tags.Where(t => t.SystemKey == Tag.UsedKey).Select(t => (int?)t.Id).SingleOrDefaultAsync(),
            });
        }
        await db.SaveChangesAsync();
        if (IpRequest.RequestedBy?.Email is { } email)
        {
            string outcome = state == IpRequestState.Approved
                ? $"a été acceptée : l'adresse {IpRequest.AssignedAddress} vous est attribuée"
                : "a été refusée";
            await Mailer.SendAsync(db, protection, [email], $"Demande d'adresse {(state == IpRequestState.Approved ? "acceptée" : "refusée")}",
                $"Votre demande d'adresse dans {IpRequest.Subnet?.Network} ({IpRequest.Description}) {outcome}." +
                (IpRequest.AdminComment is null ? "" : $"\n\nCommentaire : {IpRequest.AdminComment}") +
                $"\n\nVos demandes : {Mailer.Link("/Tools/Requests")}");
        }
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
