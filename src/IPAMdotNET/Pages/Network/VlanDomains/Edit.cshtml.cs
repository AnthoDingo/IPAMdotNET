using IPAMdotNet.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Network.VlanDomains;

[Authorize(Policy = "Admin")]
public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public VlanDomain Domain { get; set; } = new();

    [BindProperty]
    public List<int> SectionIds { get; set; } = [];

    public List<Section> AllSections { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        AllSections = await db.Sections.OrderBy(s => s.Name).ToListAsync();
        if (id is null)
        {
            return Page();
        }
        VlanDomain? domain = await db.VlanDomains.Include(d => d.Sections).SingleOrDefaultAsync(d => d.Id == id);
        if (domain is null)
        {
            return NotFound();
        }
        Domain = domain;
        SectionIds = domain.Sections.Select(s => s.Id).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Domain.Id = id ?? 0;
        Domain.Name = Domain.Name.Trim();
        if (await db.VlanDomains.AnyAsync(d => d.Name == Domain.Name && d.Id != Domain.Id))
        {
            ModelState.AddModelError("Domain.Name", "Un domaine L2 porte déjà ce nom.");
        }
        if (!ModelState.IsValid)
        {
            AllSections = await db.Sections.OrderBy(s => s.Name).ToListAsync();
            return Page();
        }
        db.Update(Domain);
        await db.SaveChangesAsync();
        VlanDomain tracked = await db.VlanDomains.Include(d => d.Sections).SingleAsync(d => d.Id == Domain.Id);
        List<Section> sections = await db.Sections.Where(s => SectionIds.Contains(s.Id)).ToListAsync();
        tracked.Sections.Clear();
        tracked.Sections.AddRange(sections);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        VlanDomain? domain = await db.VlanDomains.FindAsync(id);
        if (domain is null)
        {
            return NotFound();
        }
        if (await DeleteRefusalAsync(db, id) is { } refusal)
        {
            Domain = domain;
            AllSections = await db.Sections.OrderBy(s => s.Name).ToListAsync();
            SectionIds = await db.VlanDomains.Where(d => d.Id == id).SelectMany(d => d.Sections).Select(s => s.Id).ToListAsync();
            ModelState.AddModelError(string.Empty, refusal);
            return Page();
        }
        db.VlanDomains.Remove(domain);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    /// <summary>Motif de refus de suppression (aussi appliqué par l'API), ou null.</summary>
    public static async Task<string?> DeleteRefusalAsync(AppDbContext db, int id) =>
        id == await Vlan.DefaultDomainIdAsync(db) ? "Le domaine par défaut ne peut pas être supprimé."
        : await db.Vlans.AnyAsync(v => v.DomainId == id) ? "Impossible de supprimer un domaine L2 qui contient des VLAN."
        : null;
}
