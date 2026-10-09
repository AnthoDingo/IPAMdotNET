using IPAMdotNet.Localization;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Numerics;
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

    /// <summary>Adresses attribuées, une par ligne (ou séparées par des virgules), au plus <see cref="IpRequest.Count"/>.</summary>
    [BindProperty, MaxLength(2000), Display(Name = "Adresses attribuées")]
    public string? AssignedAddress { get; set; }

    [BindProperty, MaxLength(500), Display(Name = "Commentaire")]
    public string? AdminComment { get; set; }

    /// <summary>Le sous-réseau n'a pas assez d'adresses libres pour toute la demande.</summary>
    public bool NotEnoughFree { get; private set; }

    /// <summary>L'adresse demandée est déjà utilisée (attribuée entre-temps) : les premières libres sont proposées à la place.</summary>
    public bool RequestedTaken { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }
        // Adresse demandée si elle est encore libre, puis les premières libres du sous-réseau (comme phpIPAM).
        HashSet<BigInteger> used = (await db.IpAddresses.Where(a => a.SubnetId == IpRequest.SubnetId).Select(a => a.Address).ToListAsync())
            .Select(bytes => Ip.ToNumber(Ip.FromBytes(bytes))).ToHashSet();
        List<IPAddress> proposed = [];
        bool requestedFree = IPAddress.TryParse(IpRequest.RequestedAddress, out IPAddress? requested) && !used.Contains(Ip.ToNumber(requested));
        RequestedTaken = IpRequest.RequestedAddress is not null && !requestedFree;
        if (requestedFree)
        {
            proposed.Add(requested!);
            used.Add(Ip.ToNumber(requested!));
        }
        while (proposed.Count < IpRequest.Count && Ip.FirstFree(IpRequest.Subnet!.Network, used) is { } free)
        {
            proposed.Add(free);
            used.Add(Ip.ToNumber(free));
        }
        NotEnoughFree = proposed.Count < IpRequest.Count;
        AssignedAddress = string.Join("\n", proposed);
        return Page();
    }

    public async Task<IActionResult> OnPostApproveAsync(int id)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }
        Subnet subnet = IpRequest.Subnet!;
        List<IPAddress> addresses = [];
        List<string> errors = [];
        foreach (string text in (AssignedAddress ?? "").Split([',', ';', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!IPAddress.TryParse(text, out IPAddress? address)
                || !Ip.Contains(subnet.Network, new IPNetwork(address, address.GetAddressBytes().Length * 8)))
            {
                errors.Add(L.T("{0} : adresse invalide ou hors de {1}.", text, subnet.Network));
                continue;
            }
            (BigInteger first, BigInteger last) = Ip.UsableRange(subnet.Network);
            BigInteger value = Ip.ToNumber(address);
            byte[] bytes = Ip.ToBytes(address);
            if (subnet.IsIPv4 && (value < first || value > last))
            {
                errors.Add(L.T("{0} : l'adresse réseau et l'adresse de diffusion ne sont pas attribuables.", address));
            }
            else if (addresses.Any(a => a.Equals(address)))
            {
                errors.Add(L.T("{0} : adresse en double.", address));
            }
            else if (await db.IpAddresses.AnyAsync(a => a.SubnetId == IpRequest.SubnetId && a.Address == bytes))
            {
                errors.Add(L.T("{0} : déjà utilisée dans le sous-réseau.", address));
            }
            addresses.Add(address);
        }
        if (errors.Count == 0 && (addresses.Count == 0 || addresses.Count > IpRequest.Count))
        {
            errors.Add(L.T("Indiquez entre 1 et {0} adresse(s).", IpRequest.Count));
        }
        if (errors.Count > 0)
        {
            ModelState.AddModelError(nameof(AssignedAddress), string.Join(" ", errors));
        }
        if (!ModelState.IsValid)
        {
            return Page();
        }
        return await CloseAsync(IpRequestState.Approved, addresses);
    }

    public async Task<IActionResult> OnPostRejectAsync(int id)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }
        if (string.IsNullOrWhiteSpace(AdminComment))
        {
            ModelState.AddModelError(nameof(AdminComment), L.T("Indiquez le motif du refus."));
            return Page();
        }
        return await CloseAsync(IpRequestState.Rejected, []);
    }

    private async Task<IActionResult> CloseAsync(IpRequestState state, List<IPAddress> addresses)
    {
        IpRequest.State = state;
        IpRequest.AssignedAddress = addresses.Count == 0 ? null : string.Join(", ", addresses);
        IpRequest.AdminComment = string.IsNullOrWhiteSpace(AdminComment) ? null : AdminComment.Trim();
        IpRequest.ProcessedById = User.UserId();
        IpRequest.ProcessedAt = DateTime.UtcNow;
        if (addresses.Count > 0)
        {
            // Les adresses acceptées sont créées dans le sous-réseau, avec les informations de la demande.
            int? usedTag = await db.Tags.Where(t => t.SystemKey == Tag.UsedKey).Select(t => (int?)t.Id).SingleOrDefaultAsync();
            foreach (IPAddress address in addresses)
            {
                db.IpAddresses.Add(new IpAddress
                {
                    SubnetId = IpRequest.SubnetId,
                    Address = Ip.ToBytes(address),
                    Hostname = IpRequest.Hostname,
                    Owner = IpRequest.Owner ?? IpRequest.RequesterEmail,
                    Description = IpRequest.Description.Length > 500 ? IpRequest.Description[..500] : IpRequest.Description,
                    TagId = usedTag,
                });
            }
        }
        await db.SaveChangesAsync();
        if ((IpRequest.RequestedBy?.Email ?? IpRequest.RequesterEmail) is { } email)
        {
            string outcome = state == IpRequestState.Approved
                ? $"a été acceptée : {(addresses.Count > 1 ? "les adresses" : "l'adresse")} {IpRequest.AssignedAddress} vous {(addresses.Count > 1 ? "sont attribuées" : "est attribuée")}"
                : "a été refusée";
            await Mailer.SendAsync(db, protection, [email], $"Demande d'adresse {(state == IpRequestState.Approved ? "acceptée" : "refusée")}",
                $"Votre demande d'adresse dans {IpRequest.Subnet?.Network} ({IpRequest.Description}) {outcome}." +
                (IpRequest.AdminComment is null ? "" : $"\n\nCommentaire : {IpRequest.AdminComment}") +
                (IpRequest.RequestedBy is null ? "" : $"\n\nVos demandes : {Mailer.Link("/Tools/Requests")}"));
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
