using IPAMdotNet.Localization;
using System.Globalization;
using IPAMdotNet.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Infrastructure.Circuits.Logical;

[Authorize(Policy = "Admin")]
public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public LogicalCircuit Logical { get; set; } = new();

    /// <summary>Position de chaque circuit dans le circuit logique (clé = identifiant du circuit) ; vide = non membre.</summary>
    [BindProperty(Name = "Orders")]
    public Dictionary<int, string?> Orders { get; set; } = [];

    public List<Circuit> AllCircuits { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is not null)
        {
            LogicalCircuit? logical = await db.LogicalCircuits.Include(l => l.Members).SingleOrDefaultAsync(l => l.Id == id);
            if (logical is null)
            {
                return NotFound();
            }
            Logical = logical;
            Orders = logical.Members.ToDictionary(m => m.CircuitId, m => (string?)m.Order.ToString(CultureInfo.InvariantCulture));
        }
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Logical.Id = id ?? 0;
        LogicalCircuit? existing = id is null ? null : await db.LogicalCircuits.Include(l => l.Members).SingleOrDefaultAsync(l => l.Id == id);
        if (id is not null && existing is null)
        {
            return NotFound();
        }
        if (await db.LogicalCircuits.AnyAsync(l => l.Cid == Logical.Cid && l.Id != Logical.Id))
        {
            ModelState.AddModelError("Logical.Cid", L.T("Un circuit logique porte déjà cet identifiant."));
        }
        // Ordre saisi (entier positif), puis identifiant du circuit pour départager deux positions égales.
        List<(int CircuitId, int Order)> chosen = [];
        foreach (KeyValuePair<int, string?> pair in Orders.Where(p => !string.IsNullOrWhiteSpace(p.Value)))
        {
            if (int.TryParse(pair.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int order) && order > 0)
            {
                chosen.Add((pair.Key, order));
            }
            else
            {
                ModelState.AddModelError(string.Empty, L.T("L'ordre d'un circuit doit être un entier positif."));
            }
        }
        List<int> circuitIds = [.. chosen.OrderBy(c => c.Order).ThenBy(c => c.CircuitId).Select(c => c.CircuitId)];
        if (circuitIds.Count == 0)
        {
            ModelState.AddModelError(string.Empty, L.T("Choisissez au moins un circuit (indiquez sa position)."));
        }
        else if (await db.Circuits.CountAsync(c => circuitIds.Contains(c.Id)) != circuitIds.Count)
        {
            ModelState.AddModelError(string.Empty, L.T("Circuit inexistant."));
        }
        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }
        LogicalCircuit target = existing ?? Logical;
        if (existing is null)
        {
            db.LogicalCircuits.Add(target);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(Logical);
        }
        target.SetMembers(db, circuitIds);
        await db.SaveChangesAsync();
        return RedirectToPage("Index", null, $"logical-{target.Id}");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        LogicalCircuit? logical = await db.LogicalCircuits.Include(l => l.Members).SingleOrDefaultAsync(l => l.Id == id);
        if (logical is null)
        {
            return NotFound();
        }
        db.LogicalCircuits.Remove(logical);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    private async Task LoadAsync()
    {
        AllCircuits = await db.Circuits.Include(c => c.Provider).Include(c => c.Type)
            .OrderBy(c => c.Provider!.Name).ThenBy(c => c.Cid).ToListAsync();
        // Membres en tête, dans leur ordre.
        AllCircuits = [.. AllCircuits.OrderBy(c => Orders.TryGetValue(c.Id, out string? o) && int.TryParse(o, out int n) ? n : int.MaxValue)];
    }
}
