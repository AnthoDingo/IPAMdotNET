using IPAMdotNet.Data;
using IPAMdotNet.Networking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IPAMdotNet.Pages.Network.Nat;

[Authorize(Policy = "Admin")]
public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public NatRule Rule { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is null)
        {
            return Page();
        }
        NatRule? rule = await db.NatRules.FindAsync(id);
        if (rule is null)
        {
            return NotFound();
        }
        Rule = rule;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Rule.Id = id ?? 0;
        if (!string.IsNullOrWhiteSpace(Rule.Source))
        {
            if (Ip.TryNormalize(Rule.Source, out string source))
            {
                Rule.Source = source;
            }
            else
            {
                ModelState.AddModelError("Rule.Source", "Adresse ou réseau invalide (ex. 10.0.0.1 ou 10.0.0.0/24).");
            }
        }
        if (!string.IsNullOrWhiteSpace(Rule.Destination))
        {
            if (Ip.TryNormalize(Rule.Destination, out string destination))
            {
                Rule.Destination = destination;
            }
            else
            {
                ModelState.AddModelError("Rule.Destination", "Adresse ou réseau invalide (ex. 203.0.113.10 ou 203.0.113.0/28).");
            }
        }
        if (!ModelState.IsValid)
        {
            return Page();
        }
        db.Update(Rule);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        NatRule? rule = await db.NatRules.FindAsync(id);
        if (rule is null)
        {
            return NotFound();
        }
        db.NatRules.Remove(rule);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }
}
