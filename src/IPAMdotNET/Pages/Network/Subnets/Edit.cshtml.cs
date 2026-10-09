using IPAMdotNet.Localization;
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
using Microsoft.EntityFrameworkCore.Storage;

namespace IPAMdotNet.Pages.Network.Subnets;

/// <summary>Ouvert à tout utilisateur ayant le droit d'écriture sur la section (admin, ou permission de groupe).</summary>
public class EditModel(AppDbContext db) : PageModel
{
    private SectionAccess access = null!;

    [BindProperty]
    public Subnet Subnet { get; set; } = new();

    [BindProperty, Required(ErrorMessage = "Le sous-réseau est requis."), Display(Name = "Sous-réseau (CIDR)")]
    public string Cidr { get; set; } = "";

    public List<SelectListItem> Sections { get; private set; } = [];
    /// <summary>VLAN groupés par domaine L2 ; <c>Sections</c> = sections ouvertes (vide = toutes), pour le filtre côté navigateur.</summary>
    public List<(string? Domain, string Sections, List<SelectListItem> Vlans)> VlanGroups { get; private set; } = [];
    public List<SelectListItem> Vrfs { get; private set; } = [];
    public List<SelectListItem> Nameservers { get; private set; } = [];
    public List<SelectListItem> Locations { get; private set; } = [];
    public List<SelectListItem> Customers { get; private set; } = [];
    public List<SelectListItem> Agents { get; private set; } = [];

    // Nom explicite : sans lui, si aucun champ « Custom[…] » n'est posté, ASP.NET retombe sur le préfixe vide et lit les autres champs comme clés.
    [BindProperty(Name = CustomFieldForm.Prefix)]
    public Dictionary<int, string?> Custom { get; set; } = [];

    public List<CustomFieldInput> CustomInputs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id, int? sectionId)
    {
        access = await SectionAccess.ForAsync(db, User);
        if (id is not null)
        {
            Subnet? subnet = await db.Subnets.FindAsync(id);
            if (subnet is null)
            {
                return NotFound();
            }
            if (!access.CanWrite(subnet.SectionId))
            {
                return Forbid();
            }
            Subnet = subnet;
            Cidr = subnet.Network.ToString();
        }
        else if (sectionId is not null)
        {
            if (!access.CanWrite(sectionId.Value))
            {
                return Forbid();
            }
            Subnet.SectionId = sectionId.Value;
        }
        else if (!access.CanWriteAny)
        {
            return Forbid();
        }
        await LoadListsAsync();
        CustomInputs = await CustomFieldForm.LoadAsync(db, nameof(Subnet), id ?? 0);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Subnet.Id = id ?? 0;
        access = await SectionAccess.ForAsync(db, User);
        if (id is not null)
        {
            int? currentSection = await db.Subnets.Where(s => s.Id == id).Select(s => (int?)s.SectionId).SingleOrDefaultAsync();
            if (currentSection is null)
            {
                return NotFound();
            }
            if (!access.CanWrite(currentSection.Value))
            {
                return Forbid();
            }
        }
        if (!access.CanWrite(Subnet.SectionId))
        {
            ModelState.AddModelError("Subnet.SectionId", L.T("Vous n'avez pas le droit d'écriture sur cette section."));
        }
        if (!string.IsNullOrWhiteSpace(Cidr))
        {
            if (Ip.TryParseNetwork(Cidr, out IPNetwork network))
            {
                Subnet.SetNetwork(network);
                if (await db.Subnets.AnyAsync(s => s.SectionId == Subnet.SectionId && s.Address == Subnet.Address
                    && s.PrefixLength == Subnet.PrefixLength && s.Id != Subnet.Id))
                {
                    ModelState.AddModelError(nameof(Cidr), L.T("Ce sous-réseau existe déjà dans la section."));
                }
                // Un sous-réseau existant ne peut pas être réduit au point d'exclure ses propres adresses.
                int outside = (await db.IpAddresses.Where(a => a.SubnetId == Subnet.Id).Select(a => a.Address).ToListAsync())
                    .Count(bytes => !Ip.Contains(network, new IPNetwork(Ip.FromBytes(bytes), Ip.FromBytes(bytes).GetAddressBytes().Length * 8)));
                if (outside > 0)
                {
                    ModelState.AddModelError(nameof(Cidr), L.T("{0} adresse(s) de ce sous-réseau seraient en dehors de {1}.", outside, network));
                }
                if (Subnet.ScanAgentId is not null && !await db.RemoteAgents.AnyAsync(a => a.Id == Subnet.ScanAgentId))
                {
                    ModelState.AddModelError("Subnet.ScanAgentId", L.T("Agent de scan inconnu."));
                }
                // Le dernier scan est géré par l'agent, pas par le formulaire.
                Subnet.LastScanAt = Subnet.Id == 0 ? null : await db.Subnets.Where(s => s.Id == Subnet.Id).Select(s => s.LastScanAt).SingleOrDefaultAsync();
            }
            else if (IPNetwork.TryParse(Cidr.Trim(), out IPNetwork corrected))
            {
                ModelState.AddModelError(nameof(Cidr), L.T("Ce n'est pas une adresse réseau. Vouliez-vous dire {0} ?", corrected));
            }
            else
            {
                ModelState.AddModelError(nameof(Cidr), L.T("Sous-réseau invalide. Format attendu : 10.0.0.0/24 ou 2001:db8::/48."));
            }
        }
        if (!await db.Sections.AnyAsync(s => s.Id == Subnet.SectionId))
        {
            ModelState.AddModelError("Subnet.SectionId", L.T("Section inconnue."));
        }
        // VLAN limité aux domaines L2 ouverts à la section ; un rattachement existant reste valable tant que ni le VLAN ni la section ne changent.
        (int? VlanId, int SectionId)? stored = Subnet.Id == 0 ? null
            : await db.Subnets.Where(s => s.Id == Subnet.Id).Select(s => new ValueTuple<int?, int>(s.VlanId, s.SectionId)).SingleAsync();
        if (Subnet.VlanId is int vlanId && stored != (Subnet.VlanId, Subnet.SectionId)
            && !await Vlan.AvailableIn(db.Vlans, Subnet.SectionId).AnyAsync(v => v.Id == vlanId))
        {
            ModelState.AddModelError("Subnet.VlanId", L.T("Ce VLAN n'est pas proposé dans cette section (domaine L2 limité à d'autres sections)."));
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
        if (!(await SectionAccess.ForAsync(db, User)).CanWrite(subnet.SectionId))
        {
            return Forbid();
        }
        // Les adresses partent en cascade côté base : leurs champs personnalisés (sans clé étrangère) et liens NAT d'abord.
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync();
        await db.CustomFieldValues
            .Where(v => v.Field!.EntityType == nameof(IpAddress) && db.IpAddresses.Any(a => a.Id == v.EntityId && a.SubnetId == id))
            .ExecuteDeleteAsync();
        await db.DetachSubnetAsync(id);
        db.Subnets.Remove(subnet);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return RedirectToPage("/Sections/Index", new { id = subnet.SectionId });
    }

    private async Task LoadListsAsync()
    {
        Sections = (await db.Sections.OrderBy(s => s.Name).ToListAsync())
            .Where(s => access.CanWrite(s.Id))
            .Select(s => new SelectListItem(s.Name, s.Id.ToString())).ToList();
        // Groupés par domaine L2 dès qu'il y en a plusieurs (un même numéro peut exister dans chacun).
        List<VlanDomain> domains = await db.VlanDomains.Include(d => d.Sections).OrderBy(d => d.Name).ToListAsync();
        List<Vlan> vlans = await db.Vlans.OrderBy(v => v.Number).ToListAsync();
        VlanGroups = domains.Select(d => (domains.Count > 1 ? d.Name : null, string.Join(' ', d.Sections.Select(s => s.Id)),
            vlans.Where(v => v.DomainId == d.Id).Select(v => new SelectListItem(v.Number + " – " + v.Name, v.Id.ToString())).ToList()))
            .Where(g => g.Item3.Count > 0).ToList();
        Vrfs = await db.Vrfs.OrderBy(v => v.Name)
            .Select(v => new SelectListItem(v.Name, v.Id.ToString())).ToListAsync();
        Nameservers = await db.Nameservers.OrderBy(n => n.Name)
            .Select(n => new SelectListItem(n.Name, n.Id.ToString())).ToListAsync();
        Locations = await db.Locations.OrderBy(l => l.Name)
            .Select(l => new SelectListItem(l.Name, l.Id.ToString())).ToListAsync();
        Customers = await db.Customers.OrderBy(c => c.Name)
            .Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync();
        Agents = await db.RemoteAgents.OrderBy(a => a.Name)
            .Select(a => new SelectListItem(a.Name, a.Id.ToString())).ToListAsync();
    }
}
