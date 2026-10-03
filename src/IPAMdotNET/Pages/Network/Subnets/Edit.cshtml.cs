using System.ComponentModel.DataAnnotations;
using System.Net;
using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using IPAMdotNet.Networking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Network.Subnets;

[Authorize(Policy = "Admin")]
public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public Subnet Subnet { get; set; } = new();

    [BindProperty, Required(ErrorMessage = "Le sous-réseau est requis."), Display(Name = "Sous-réseau (CIDR)")]
    public string Cidr { get; set; } = "";

    public List<SelectListItem> Sections { get; private set; } = [];
    public List<SelectListItem> Vlans { get; private set; } = [];
    public List<SelectListItem> Vrfs { get; private set; } = [];
    public List<SelectListItem> Nameservers { get; private set; } = [];
    public List<SelectListItem> Locations { get; private set; } = [];
    public List<SelectListItem> Customers { get; private set; } = [];

    // Nom explicite : sans lui, si aucun champ « Custom[…] » n'est posté, ASP.NET retombe sur le préfixe vide et lit les autres champs comme clés.
    [BindProperty(Name = CustomFieldForm.Prefix)]
    public Dictionary<int, string?> Custom { get; set; } = [];

    public List<CustomFieldInput> CustomInputs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id, int? sectionId)
    {
        if (id is not null)
        {
            Subnet? subnet = await db.Subnets.FindAsync(id);
            if (subnet is null)
            {
                return NotFound();
            }
            Subnet = subnet;
            Cidr = subnet.Network.ToString();
        }
        else if (sectionId is not null)
        {
            Subnet.SectionId = sectionId.Value;
        }
        await LoadListsAsync();
        CustomInputs = await CustomFieldForm.LoadAsync(db, nameof(Subnet), id ?? 0);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Subnet.Id = id ?? 0;
        if (!string.IsNullOrWhiteSpace(Cidr))
        {
            if (Ip.TryParseNetwork(Cidr, out IPNetwork network))
            {
                Subnet.SetNetwork(network);
                if (await db.Subnets.AnyAsync(s => s.SectionId == Subnet.SectionId && s.Address == Subnet.Address
                    && s.PrefixLength == Subnet.PrefixLength && s.Id != Subnet.Id))
                {
                    ModelState.AddModelError(nameof(Cidr), "Ce sous-réseau existe déjà dans la section.");
                }
            }
            else if (IPNetwork.TryParse(Cidr.Trim(), out IPNetwork corrected))
            {
                ModelState.AddModelError(nameof(Cidr), $"Ce n'est pas une adresse réseau. Vouliez-vous dire {corrected} ?");
            }
            else
            {
                ModelState.AddModelError(nameof(Cidr), "Sous-réseau invalide. Format attendu : 10.0.0.0/24 ou 2001:db8::/48.");
            }
        }
        if (!await db.Sections.AnyAsync(s => s.Id == Subnet.SectionId))
        {
            ModelState.AddModelError("Subnet.SectionId", "Section inconnue.");
        }
        List<CustomField> customFields = await CustomFieldForm.DefinitionsAsync(db, nameof(Subnet));
        Dictionary<int, string?> customValues = CustomFieldForm.Validate(customFields, Custom, ModelState);
        if (!ModelState.IsValid)
        {
            await LoadListsAsync();
            CustomInputs = CustomFieldForm.FromPosted(customFields, Custom);
            return Page();
        }
        db.Update(Subnet);
        await db.SaveChangesAsync();
        await CustomFieldForm.SaveAsync(db, Subnet.Id, customValues);
        return RedirectToPage("Details", new { id = Subnet.Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        Subnet? subnet = await db.Subnets.FindAsync(id);
        if (subnet is null)
        {
            return NotFound();
        }
        db.Subnets.Remove(subnet);
        await db.SaveChangesAsync();
        return RedirectToPage("/Sections/Index", new { id = subnet.SectionId });
    }

    private async Task LoadListsAsync()
    {
        Sections = await db.Sections.OrderBy(s => s.Name)
            .Select(s => new SelectListItem(s.Name, s.Id.ToString())).ToListAsync();
        Vlans = await db.Vlans.OrderBy(v => v.Number)
            .Select(v => new SelectListItem(v.Number + " – " + v.Name, v.Id.ToString())).ToListAsync();
        Vrfs = await db.Vrfs.OrderBy(v => v.Name)
            .Select(v => new SelectListItem(v.Name, v.Id.ToString())).ToListAsync();
        Nameservers = await db.Nameservers.OrderBy(n => n.Name)
            .Select(n => new SelectListItem(n.Name, n.Id.ToString())).ToListAsync();
        Locations = await db.Locations.OrderBy(l => l.Name)
            .Select(l => new SelectListItem(l.Name, l.Id.ToString())).ToListAsync();
        Customers = await db.Customers.OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync();
    }
}
