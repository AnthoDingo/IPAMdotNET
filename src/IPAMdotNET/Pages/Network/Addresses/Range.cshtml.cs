using IPAMdotNet.Localization;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Numerics;
using IPAMdotNet.Data;
using IPAMdotNet.Networking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Network.Addresses;

/// <summary>Ajout en masse : toutes les adresses attribuables d'une plage, avec une étiquette et une description communes.</summary>
public class RangeModel(AppDbContext db) : PageModel
{
    /// <summary>Taille maximale d'une plage (un /20) : au-delà, la saisie et le journal deviennent démesurés.</summary>
    public const int MaxAddresses = 4096;

    [BindProperty, Required(ErrorMessage = "La première adresse est requise."), Display(Name = "Première adresse")]
    public string First { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "La dernière adresse est requise."), Display(Name = "Dernière adresse")]
    public string Last { get; set; } = "";

    [BindProperty, Display(Name = "Étiquette")]
    public int? TagId { get; set; }

    [BindProperty, MaxLength(500), Display(Name = "Description")]
    public string? Description { get; set; }

    [BindProperty, MaxLength(100), Display(Name = "Propriétaire")]
    public string? Owner { get; set; }

    public Subnet Subnet { get; private set; } = new();
    public List<SelectListItem> Tags { get; private set; } = [];

    [TempData]
    public string? Message { get; set; }

    public async Task<IActionResult> OnGetAsync(int subnetId, string? first, string? last)
    {
        IActionResult? denied = await LoadAsync(subnetId);
        if (denied is not null)
        {
            return denied;
        }
        // Par défaut : depuis l'adresse choisie (plage libre de la liste) ou le début, jusqu'à la fin ou MaxAddresses.
        (BigInteger start, BigInteger end) = Ip.UsableRange(Subnet.Network);
        if (first is not null && IPAddress.TryParse(first, out IPAddress? from))
        {
            start = BigInteger.Max(start, Ip.ToNumber(from));
        }
        First = Ip.FromNumber(start, Subnet.IsIPv4).ToString();
        Last = last ?? Ip.FromNumber(BigInteger.Min(end, start + MaxAddresses - 1), Subnet.IsIPv4).ToString();
        TagId = await db.Tags.Where(t => t.SystemKey == Tag.ReservedKey).Select(t => (int?)t.Id).SingleOrDefaultAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int subnetId)
    {
        IActionResult? denied = await LoadAsync(subnetId);
        if (denied is not null)
        {
            return denied;
        }
        (BigInteger usableFirst, BigInteger usableLast) = Ip.UsableRange(Subnet.Network);
        BigInteger? start = Parse(First, nameof(First), usableFirst, usableLast);
        BigInteger? end = Parse(Last, nameof(Last), usableFirst, usableLast);
        if (start is not null && end is not null)
        {
            if (end < start)
            {
                ModelState.AddModelError(nameof(Last), L.T("La dernière adresse doit suivre la première."));
            }
            else if (end - start + 1 > MaxAddresses)
            {
                ModelState.AddModelError(nameof(Last), L.T("{0} adresses au plus par ajout.", MaxAddresses));
            }
        }
        if (!ModelState.IsValid)
        {
            return Page();
        }

        HashSet<BigInteger> existing = (await db.IpAddresses.Where(a => a.SubnetId == Subnet.Id).Select(a => a.Address).ToListAsync())
            .Select(bytes => Ip.ToNumber(Ip.FromBytes(bytes))).ToHashSet();
        int added = 0;
        for (BigInteger n = start!.Value; n <= end!.Value; n++)
        {
            if (existing.Contains(n))
            {
                continue;
            }
            db.IpAddresses.Add(new IpAddress
            {
                SubnetId = Subnet.Id,
                Address = Ip.ToBytes(Ip.FromNumber(n, Subnet.IsIPv4)),
                TagId = TagId,
                Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
                Owner = string.IsNullOrWhiteSpace(Owner) ? null : Owner.Trim(),
            });
            added++;
        }
        await db.SaveChangesAsync();
        int skipped = (int)(end.Value - start.Value + 1) - added;
        Message = L.T("{0} adresse(s) ajoutée(s)", added) + (skipped > 0 ? L.T(", {0} déjà présente(s) ignorée(s).", skipped) : ".");
        return RedirectToPage("/Network/Subnets/Details", new { id = Subnet.Id });
    }

    /// <summary>Adresse saisie, dans la plage attribuable du sous-réseau ; null (et erreur) sinon.</summary>
    private BigInteger? Parse(string text, string field, BigInteger usableFirst, BigInteger usableLast)
    {
        if (!IPAddress.TryParse(text.Trim(), out IPAddress? address) || (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) != Subnet.IsIPv4)
        {
            ModelState.AddModelError(field, L.T("Adresse invalide."));
            return null;
        }
        BigInteger value = Ip.ToNumber(address);
        if (value < usableFirst || value > usableLast)
        {
            ModelState.AddModelError(field, L.T("Hors de la plage attribuable de {0}.", Subnet.Network));
            return null;
        }
        return value;
    }

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
        Tags = await db.Tags.OrderByDescending(t => t.Locked).ThenBy(t => t.Name).Select(t => new SelectListItem(t.Name, t.Id.ToString())).ToListAsync();
        return null;
    }
}
