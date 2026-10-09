using System.ComponentModel.DataAnnotations;
using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using IPAMdotNet.Pages.Tools.Requests;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages;

public sealed class PublicRequestInput
{
    [Required(ErrorMessage = "L'adresse e-mail est requise."), MaxLength(200), EmailAddress(ErrorMessage = "Adresse e-mail invalide."), Display(Name = "Votre adresse e-mail")]
    public string Email { get; set; } = "";

    [Range(1, int.MaxValue, ErrorMessage = "Le sous-réseau est requis."), Display(Name = "Sous-réseau")]
    public int SubnetId { get; set; }

    [Range(1, IpRequest.MaxCount, ErrorMessage = "Entre 1 et 32 adresses."), Display(Name = "Nombre d'adresses")]
    public int Count { get; set; } = 1;

    [Required(ErrorMessage = "Le motif est requis."), MaxLength(500), Display(Name = "Description")]
    public string Description { get; set; } = "";
}

/// <summary>
/// Demande d'adresse sans compte, depuis la page de connexion (comme phpIPAM) : sous-réseaux ouverts aux demandes,
/// réponse envoyée à l'adresse e-mail saisie. Volontairement hors de /Account : elle écrit des colonnes récentes,
/// elle doit donc rester derrière la garde /update.
/// </summary>
[IpRequestsEnabled]
[EnableRateLimiting("login")]
public class RequestAddressModel(AppDbContext db, IDataProtectionProvider protection) : PageModel
{
    [BindProperty]
    public PublicRequestInput Input { get; set; } = new();

    public List<SelectListItem> Subnets { get; private set; } = [];
    public bool Sent { get; private set; }

    public async Task OnGetAsync() => await LoadSubnetsAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        Subnet? subnet = await db.Subnets.FindAsync(Input.SubnetId);
        if (subnet is null || !subnet.AllowRequests)
        {
            ModelState.AddModelError("Input.SubnetId", "Ce sous-réseau n'accepte pas les demandes.");
        }
        if (!ModelState.IsValid)
        {
            await LoadSubnetsAsync();
            return Page();
        }

        string email = Input.Email.Trim();
        IpRequest request = new()
        {
            SubnetId = subnet!.Id,
            RequesterEmail = email,
            Count = Input.Count,
            Description = Input.Description.Trim(),
            State = IpRequestState.Pending,
            RequestedAt = DateTime.UtcNow,
        };
        db.IpRequests.Add(request);
        db.AuditUserName = email;
        await db.SaveChangesAsync();
        await NewModel.NotifyAdminsAsync(db, protection, request, subnet, email);
        Sent = true;
        return Page();
    }

    private async Task LoadSubnetsAsync()
    {
        // Comme phpIPAM : tous les sous-réseaux ouverts aux demandes (l'ouverture est un choix explicite de l'admin).
        List<Subnet> subnets = await db.Subnets.Include(s => s.Section).Where(s => s.AllowRequests)
            .OrderBy(s => s.Section!.Name).ThenBy(s => s.Address).ThenBy(s => s.PrefixLength)
            .ToListAsync();
        Subnets = subnets
            .Select(s => new SelectListItem($"{s.Section?.Name} — {s.Network}{(s.Description is null ? "" : $" ({s.Description})")}", s.Id.ToString()))
            .ToList();
    }
}
