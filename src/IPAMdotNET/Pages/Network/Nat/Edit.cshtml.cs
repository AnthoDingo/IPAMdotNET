using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using IPAMdotNet.Networking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Network.Nat;

[Authorize(Policy = "Admin")]
public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public NatRule Rule { get; set; } = new();

    /// <summary>Objet lié à chaque côté : « a:id », « s:id », « none » (texte libre) ou vide (liaison automatique).</summary>
    [BindProperty]
    public string? SourceChoice { get; set; }

    [BindProperty]
    public string? DestinationChoice { get; set; }

    /// <summary>Plusieurs objets correspondent au texte saisi : choix proposé.</summary>
    public List<NatCandidate>? SourceCandidates { get; private set; }
    public List<NatCandidate>? DestinationCandidates { get; private set; }

    /// <summary>Libellé de l'objet lié, pour l'affichage.</summary>
    public string? SourceLinked { get; private set; }
    public string? DestinationLinked { get; private set; }

    public List<SelectListItem> Devices { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is not null)
        {
            NatRule? rule = await db.NatRules.FindAsync(id);
            if (rule is null)
            {
                return NotFound();
            }
            Rule = rule;
            SourceChoice = NatLinks.Key(rule.SourceSubnetId, rule.SourceAddressId);
            DestinationChoice = NatLinks.Key(rule.DestinationSubnetId, rule.DestinationAddressId);
        }
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Rule.Id = id ?? 0;
        NatSide source = await SideAsync(Rule.Source, SourceChoice);
        NatSide destination = await SideAsync(Rule.Destination, DestinationChoice);
        (Rule.Source, Rule.SourceSubnetId, Rule.SourceAddressId) = (source.Text, source.SubnetId, source.AddressId);
        (Rule.Destination, Rule.DestinationSubnetId, Rule.DestinationAddressId) = (destination.Text, destination.SubnetId, destination.AddressId);
        if (!string.IsNullOrWhiteSpace(Rule.Source) && source.Error is not null)
        {
            ModelState.AddModelError("Rule.Source", source.Error);
            SourceCandidates = source.Candidates;
        }
        if (!string.IsNullOrWhiteSpace(Rule.Destination) && destination.Error is not null)
        {
            ModelState.AddModelError("Rule.Destination", destination.Error);
            DestinationCandidates = destination.Candidates;
        }
        if (Rule.DeviceId is int deviceId && !await db.Devices.AnyAsync(d => d.Id == deviceId))
        {
            ModelState.AddModelError("Rule.DeviceId", "Équipement inexistant.");
        }
        if (!ModelState.IsValid)
        {
            SourceChoice = NatLinks.Key(Rule.SourceSubnetId, Rule.SourceAddressId);
            DestinationChoice = NatLinks.Key(Rule.DestinationSubnetId, Rule.DestinationAddressId);
            await LoadAsync();
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

    /// <summary>Un lien existant ne vaut plus si le texte a été modifié à la main : nouvelle liaison automatique.</summary>
    private async Task<NatSide> SideAsync(string? text, string? choice)
    {
        NatSide side = await NatLinks.ResolveAsync(db, text, choice);
        bool linked = choice is not null && choice != NatLinks.None && side.Error is null;
        if (linked && Ip.TryNormalize(text, out string typed) && typed != side.Text)
        {
            side = await NatLinks.ResolveAsync(db, text, null);
        }
        return side;
    }

    private async Task LoadAsync()
    {
        Devices = await db.Devices.OrderBy(d => d.Hostname).Select(d => new SelectListItem(d.Hostname, d.Id.ToString())).ToListAsync();
        SourceLinked = await LinkedLabelAsync(Rule.Source, SourceChoice);
        DestinationLinked = await LinkedLabelAsync(Rule.Destination, DestinationChoice);
    }

    private async Task<string?> LinkedLabelAsync(string text, string? key) =>
        key is null || key == NatLinks.None || !Ip.TryNormalize(text, out string normalized) ? null
            : (await NatLinks.CandidatesAsync(db, normalized)).FirstOrDefault(c => c.Key == key)?.Label;
}
