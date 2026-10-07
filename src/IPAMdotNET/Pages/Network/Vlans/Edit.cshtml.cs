using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Network.Vlans;

[Authorize(Policy = "Admin")]
public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public Vlan Vlan { get; set; } = new();

    // Nom explicite : sans lui, si aucun champ « Custom[…] » n'est posté, ASP.NET retombe sur le préfixe vide et lit les autres champs comme clés.
    [BindProperty(Name = CustomFieldForm.Prefix)]
    public Dictionary<int, string?> Custom { get; set; } = [];

    public List<CustomFieldInput> CustomInputs { get; private set; } = [];
    public List<SelectListItem> Domains { get; private set; } = [];

    /// <summary>Création : domaine proposé (celui de la liste d'origine), sinon le domaine par défaut.</summary>
    public async Task<IActionResult> OnGetAsync(int? id, int? domain)
    {
        Vlan.DomainId = domain ?? await Vlan.DefaultDomainIdAsync(db);
        if (id is not null)
        {
            Vlan? vlan = await db.Vlans.FindAsync(id);
            if (vlan is null)
            {
                return NotFound();
            }
            Vlan = vlan;
        }
        CustomInputs = await CustomFieldForm.LoadAsync(db, nameof(Vlan), id ?? 0);
        await LoadDomainsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Vlan.Id = id ?? 0;
        if (!await db.VlanDomains.AnyAsync(d => d.Id == Vlan.DomainId))
        {
            ModelState.AddModelError("Vlan.DomainId", "Domaine L2 inexistant.");
        }
        else if (await db.Vlans.AnyAsync(v => v.DomainId == Vlan.DomainId && v.Number == Vlan.Number && v.Id != Vlan.Id))
        {
            ModelState.AddModelError("Vlan.Number", "Ce numéro de VLAN existe déjà dans ce domaine.");
        }
        List<CustomField> customFields = await CustomFieldForm.DefinitionsAsync(db, nameof(Vlan));
        Dictionary<int, string?> customValues = CustomFieldForm.Validate(customFields, Custom, ModelState);
        if (!ModelState.IsValid)
        {
            CustomInputs = CustomFieldForm.FromPosted(customFields, Custom);
            await LoadDomainsAsync();
            return Page();
        }
        db.Update(Vlan);
        await db.SaveChangesAsync();
        await CustomFieldForm.SaveAsync(db, Vlan.Id, customValues);
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        Vlan? vlan = await db.Vlans.FindAsync(id);
        if (vlan is null)
        {
            return NotFound();
        }
        db.Vlans.Remove(vlan);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    private async Task LoadDomainsAsync() =>
        Domains = await db.VlanDomains.OrderBy(d => d.Name).Select(d => new SelectListItem(d.Name, d.Id.ToString())).ToListAsync();
}
