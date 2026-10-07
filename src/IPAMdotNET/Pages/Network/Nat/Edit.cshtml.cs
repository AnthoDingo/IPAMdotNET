using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using IPAMdotNet.Networking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Network.Nat;

/// <summary>Un côté du formulaire : une entrée par ligne, avec l'objet lié (ou les objets proposés) de chaque ligne.</summary>
public sealed class NatSideForm
{
    public string? Text { get; set; }

    /// <summary>Par ligne : « a:id », « s:id », « none » (texte libre) ou vide (liaison automatique).</summary>
    public List<string?> Choices { get; set; } = [];

    public Dictionary<int, List<NatCandidate>> Candidates { get; } = [];
    public Dictionary<int, string> Linked { get; } = [];
}

[Authorize(Policy = "Admin")]
public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public NatRule Rule { get; set; } = new();

    [BindProperty]
    public NatSideForm Source { get; set; } = new();

    [BindProperty]
    public NatSideForm Destination { get; set; } = new();

    public List<SelectListItem> Devices { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is not null)
        {
            NatRule? rule = await db.NatRules.Include(n => n.Objects).SingleOrDefaultAsync(n => n.Id == id);
            if (rule is null)
            {
                return NotFound();
            }
            Rule = rule;
            Source = await FormAsync(rule.Sources);
            Destination = await FormAsync(rule.Destinations);
        }
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Rule.Id = id ?? 0;
        if (id is not null && !await db.NatRules.AnyAsync(n => n.Id == id))
        {
            return NotFound();
        }
        List<NatRuleObject> objects = [
            .. await ResolveAsync(Source, NatSideKind.Source, "Source.Text", "source"),
            .. await ResolveAsync(Destination, NatSideKind.Destination, "Destination.Text", "destination"),
        ];
        if (Rule.DeviceId is int deviceId && !await db.Devices.AnyAsync(d => d.Id == deviceId))
        {
            ModelState.AddModelError("Rule.DeviceId", "Équipement inexistant.");
        }
        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }
        NatRule rule = id is null ? Rule : await db.NatRules.Include(n => n.Objects).SingleAsync(n => n.Id == id);
        if (id is not null)
        {
            db.Entry(rule).CurrentValues.SetValues(Rule);
            db.NatRuleObjects.RemoveRange(rule.Objects);
        }
        else
        {
            db.NatRules.Add(rule);
        }
        rule.Objects.AddRange(objects);
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

    /// <summary>
    /// Chaque ligne est résolue avec le choix de même rang ; un lien ne vaut plus si le texte de la ligne a changé
    /// (nouvelle liaison automatique). Les erreurs et les objets proposés sont rangés par ligne pour le réaffichage.
    /// </summary>
    private async Task<List<NatRuleObject>> ResolveAsync(NatSideForm form, NatSideKind kind, string field, string label)
    {
        // Valeur postée oubliée : la zone est réaffichée dans la forme canonique (les erreurs ajoutées ensuite restent).
        ModelState.Remove(field);
        List<string> lines = NatLinks.Lines(form.Text);
        if (lines.Count == 0)
        {
            ModelState.AddModelError(field, $"Au moins une {label} est requise.");
        }
        List<NatRuleObject> objects = [];
        List<string?> choices = [];
        for (int i = 0; i < lines.Count; i++)
        {
            string? choice = form.Choices.ElementAtOrDefault(i);
            NatSide side = await NatLinks.ResolveAsync(db, lines[i], choice);
            if (choice is not null && choice != NatLinks.None && side.Error is null && Ip.TryNormalize(lines[i], out string typed) && typed != side.Text)
            {
                side = await NatLinks.ResolveAsync(db, lines[i], null);
            }
            if (side.Error is not null)
            {
                ModelState.AddModelError(field, $"« {lines[i]} » : {side.Error}");
                if (side.Candidates is not null)
                {
                    form.Candidates[i] = side.Candidates;
                }
            }
            choices.Add(NatLinks.Key(side.SubnetId, side.AddressId) ?? (choice == NatLinks.None ? NatLinks.None : null));
            objects.Add(new NatRuleObject { Side = kind, Text = side.Text, SubnetId = side.SubnetId, AddressId = side.AddressId });
        }
        // Réaffichage : une ligne par entrée, dans la forme canonique.
        form.Text = string.Join('\n', objects.Select(o => o.Text));
        form.Choices = choices;
        return objects;
    }

    /// <summary>Formulaire d'un côté enregistré (texte actualisé depuis les objets liés).</summary>
    private async Task<NatSideForm> FormAsync(IEnumerable<NatRuleObject> objects)
    {
        NatSideForm form = new();
        List<string> lines = [];
        foreach (NatRuleObject item in objects)
        {
            string? key = NatLinks.Key(item.SubnetId, item.AddressId);
            NatSide side = await NatLinks.ResolveAsync(db, item.Text, key ?? NatLinks.None);
            lines.Add(side.Text);
            form.Choices.Add(key);
        }
        form.Text = string.Join('\n', lines);
        return form;
    }

    private async Task LoadAsync()
    {
        Devices = await db.Devices.OrderBy(d => d.Hostname).Select(d => new SelectListItem(d.Hostname, d.Id.ToString())).ToListAsync();
        foreach (NatSideForm form in new[] { Source, Destination })
        {
            List<string> lines = NatLinks.Lines(form.Text);
            for (int i = 0; i < lines.Count; i++)
            {
                if (form.Choices.ElementAtOrDefault(i) is { } key && key != NatLinks.None && Ip.TryNormalize(lines[i], out string normalized)
                    && (await NatLinks.CandidatesAsync(db, normalized)).FirstOrDefault(c => c.Key == key) is { } candidate)
                {
                    form.Linked[i] = candidate.Label;
                }
            }
        }
    }
}
