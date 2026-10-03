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

/// <summary>Adresse IP d'un sous-réseau : réservée aux utilisateurs ayant le droit d'écriture sur la section.</summary>
public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public IpAddress Entry { get; set; } = new();

    [BindProperty, Required(ErrorMessage = "L'adresse est requise."), Display(Name = "Adresse IP")]
    public string Ip { get; set; } = "";

    public Subnet Subnet { get; private set; } = new();
    public List<SelectListItem> Tags { get; private set; } = [];
    public List<SelectListItem> Devices { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id, int? subnetId)
    {
        if (id is not null)
        {
            IpAddress? entry = await db.IpAddresses.FindAsync(id);
            if (entry is null)
            {
                return NotFound();
            }
            Entry = entry;
            Ip = entry.Value.ToString();
        }
        else
        {
            Entry.SubnetId = subnetId ?? 0;
        }
        IActionResult? denied = await LoadAsync();
        if (denied is not null)
        {
            return denied;
        }
        if (id is null)
        {
            // Nouvelle adresse : première adresse libre, étiquette « Utilisée ».
            HashSet<BigInteger> used = (await db.IpAddresses.Where(a => a.SubnetId == Subnet.Id).Select(a => a.Address).ToListAsync())
                .Select(bytes => Networking.Ip.ToNumber(Networking.Ip.FromBytes(bytes))).ToHashSet();
            Ip = Networking.Ip.FirstFree(Subnet.Network, used)?.ToString() ?? "";
            Entry.TagId = await db.Tags.Where(t => t.SystemKey == Tag.UsedKey).Select(t => (int?)t.Id).SingleOrDefaultAsync();
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Entry.Id = id ?? 0;
        if (id is not null)
        {
            // Le sous-réseau d'une adresse existante ne change pas par le formulaire.
            int? subnetId = await db.IpAddresses.Where(a => a.Id == id).Select(a => (int?)a.SubnetId).SingleOrDefaultAsync();
            if (subnetId is null)
            {
                return NotFound();
            }
            Entry.SubnetId = subnetId.Value;
        }
        IActionResult? denied = await LoadAsync();
        if (denied is not null)
        {
            return denied;
        }

        IPNetwork network = Subnet.Network;
        if (!IPAddress.TryParse(Ip.Trim(), out IPAddress? address) || !Networking.Ip.Contains(network, new IPNetwork(address, address.GetAddressBytes().Length * 8)))
        {
            ModelState.AddModelError(nameof(Ip), $"Adresse invalide ou hors de {network}.");
        }
        else
        {
            (BigInteger first, BigInteger last) = Networking.Ip.UsableRange(network);
            BigInteger value = Networking.Ip.ToNumber(address);
            if (Subnet.IsIPv4 && (value < first || value > last))
            {
                ModelState.AddModelError(nameof(Ip), "L'adresse réseau et l'adresse de diffusion ne sont pas attribuables.");
            }
            Entry.Address = Networking.Ip.ToBytes(address);
            if (await db.IpAddresses.AnyAsync(a => a.SubnetId == Entry.SubnetId && a.Address == Entry.Address && a.Id != Entry.Id))
            {
                ModelState.AddModelError(nameof(Ip), "Cette adresse existe déjà dans le sous-réseau.");
            }
        }
        if (!string.IsNullOrWhiteSpace(Entry.MacAddress))
        {
            string? mac = IpAddress.NormalizeMac(Entry.MacAddress);
            if (mac is null)
            {
                ModelState.AddModelError("Entry.MacAddress", "Adresse MAC invalide (ex. 00:11:22:aa:bb:cc).");
            }
            Entry.MacAddress = mac ?? Entry.MacAddress;
        }
        else
        {
            Entry.MacAddress = null;
        }
        if (!ModelState.IsValid)
        {
            return Page();
        }
        if (id is not null)
        {
            // LastSeen appartient à l'agent de scan : conservé.
            Entry.LastSeen = await db.IpAddresses.Where(a => a.Id == id).Select(a => a.LastSeen).SingleAsync();
        }
        db.Update(Entry);
        await db.SaveChangesAsync();
        return Redirect(Url.Page("/Network/Subnets/Details", new { id = Entry.SubnetId }) + $"#address-{Entry.Id}");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        IpAddress? entry = await db.IpAddresses.FindAsync(id);
        if (entry is null)
        {
            return NotFound();
        }
        Entry = entry;
        IActionResult? denied = await LoadAsync();
        if (denied is not null)
        {
            return denied;
        }
        db.IpAddresses.Remove(entry);
        await db.SaveChangesAsync();
        return RedirectToPage("/Network/Subnets/Details", new { id = entry.SubnetId });
    }

    /// <summary>Charge le sous-réseau et les listes ; renvoie un résultat d'erreur si introuvable ou interdit.</summary>
    private async Task<IActionResult?> LoadAsync()
    {
        Subnet? subnet = await db.Subnets.Include(s => s.Section).SingleOrDefaultAsync(s => s.Id == Entry.SubnetId);
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
        Devices = await db.Devices.OrderBy(d => d.Hostname).Select(d => new SelectListItem(d.Hostname, d.Id.ToString())).ToListAsync();
        return null;
    }
}
