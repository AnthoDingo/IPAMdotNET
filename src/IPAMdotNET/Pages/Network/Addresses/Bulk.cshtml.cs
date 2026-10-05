using System.ComponentModel.DataAnnotations;
using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Network.Addresses;

/// <summary>
/// Modification ou suppression des adresses cochées dans la fiche d'un sous-réseau. Chaque case porte une ou plusieurs
/// adresses (« 12,13,14 » pour une plage regroupée). Un champ laissé sur « inchangé » ou vide n'est pas modifié.
/// </summary>
public class BulkModel(AppDbContext db) : PageModel
{
    /// <summary>Valeur des listes « inchangé » (une liste vide signifie « aucun »).</summary>
    public const string Unchanged = "-";

    [BindProperty]
    public List<string> Ids { get; set; } = [];

    [BindProperty, Display(Name = "Étiquette")]
    public string? TagId { get; set; } = Unchanged;

    [BindProperty, Display(Name = "Équipement")]
    public string? DeviceId { get; set; } = Unchanged;

    [BindProperty, Display(Name = "Exclure du ping")]
    public string? ExcludePing { get; set; } = Unchanged;

    [BindProperty, MaxLength(500), Display(Name = "Description")]
    public string? Description { get; set; }

    [BindProperty, MaxLength(100), Display(Name = "Propriétaire")]
    public string? Owner { get; set; }

    public Subnet Subnet { get; private set; } = new();
    public List<IpAddress> Addresses { get; private set; } = [];
    public List<SelectListItem> Tags { get; private set; } = [];
    public List<SelectListItem> Devices { get; private set; } = [];

    [TempData]
    public string? Message { get; set; }

    /// <summary>Depuis la fiche du sous-réseau : affiche le formulaire pour la sélection.</summary>
    public async Task<IActionResult> OnPostAsync(int subnetId)
    {
        IActionResult? denied = await LoadAsync(subnetId);
        if (denied is not null)
        {
            return denied;
        }
        ModelState.Clear();
        return Addresses.Count == 0 ? NothingSelected(subnetId) : Page();
    }

    public async Task<IActionResult> OnPostApplyAsync(int subnetId)
    {
        IActionResult? denied = await LoadAsync(subnetId);
        if (denied is not null)
        {
            return denied;
        }
        if (Addresses.Count == 0)
        {
            return NothingSelected(subnetId);
        }
        if (TagId is not (Unchanged or "" or null) && !Tags.Any(t => t.Value == TagId))
        {
            ModelState.AddModelError(nameof(TagId), "Étiquette inconnue.");
        }
        if (DeviceId is not (Unchanged or "" or null) && !Devices.Any(d => d.Value == DeviceId))
        {
            ModelState.AddModelError(nameof(DeviceId), "Équipement inconnu.");
        }
        if (!ModelState.IsValid)
        {
            return Page();
        }
        foreach (IpAddress address in Addresses)
        {
            if (TagId != Unchanged)
            {
                address.TagId = int.TryParse(TagId, out int tag) ? tag : null;
            }
            if (DeviceId != Unchanged)
            {
                address.DeviceId = int.TryParse(DeviceId, out int device) ? device : null;
            }
            if (ExcludePing != Unchanged)
            {
                address.ExcludePing = ExcludePing == "true";
            }
            if (!string.IsNullOrWhiteSpace(Description))
            {
                address.Description = Description.Trim();
            }
            if (!string.IsNullOrWhiteSpace(Owner))
            {
                address.Owner = Owner.Trim();
            }
        }
        await db.SaveChangesAsync();
        Message = $"{Addresses.Count} adresse(s) modifiée(s).";
        return RedirectToPage("/Network/Subnets/Details", new { id = subnetId });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int subnetId)
    {
        IActionResult? denied = await LoadAsync(subnetId);
        if (denied is not null)
        {
            return denied;
        }
        if (Addresses.Count == 0)
        {
            return NothingSelected(subnetId);
        }
        // Suppression suivie (pas ExecuteDelete) : journalisée, et les champs personnalisés partent avec.
        db.IpAddresses.RemoveRange(Addresses);
        await db.SaveChangesAsync();
        Message = $"{Addresses.Count} adresse(s) supprimée(s).";
        return RedirectToPage("/Network/Subnets/Details", new { id = subnetId });
    }

    private RedirectToPageResult NothingSelected(int subnetId)
    {
        Message = "Aucune adresse sélectionnée.";
        return RedirectToPage("/Network/Subnets/Details", new { id = subnetId });
    }

    /// <summary>Sous-réseau, droit d'écriture, et adresses sélectionnées appartenant bien à ce sous-réseau.</summary>
    private async Task<IActionResult?> LoadAsync(int subnetId)
    {
        Subnet? subnet = await db.Subnets.Include(s => s.Section).SingleOrDefaultAsync(s => s.Id == subnetId);
        if (subnet is null)
        {
            return NotFound();
        }
        if (!(await SectionAccess.ForAsync(db, User)).CanWrite(subnet.SectionId))
        {
            return Forbid();
        }
        Subnet = subnet;
        HashSet<int> selected = Ids.SelectMany(v => v.Split(',')).Select(v => int.TryParse(v, out int id) ? id : 0).ToHashSet();
        // Filtre en mémoire : une sélection de milliers d'identifiants dépasserait la limite de paramètres de SQL Server.
        Addresses = (await db.IpAddresses.Include(a => a.Tag).Where(a => a.SubnetId == subnetId).OrderBy(a => a.Address).ToListAsync())
            .Where(a => selected.Contains(a.Id)).ToList();
        Tags = await db.Tags.OrderByDescending(t => t.Locked).ThenBy(t => t.Name).Select(t => new SelectListItem(t.Name, t.Id.ToString())).ToListAsync();
        Devices = await db.Devices.OrderBy(d => d.Hostname).Select(d => new SelectListItem(d.Hostname, d.Id.ToString())).ToListAsync();
        return null;
    }
}
