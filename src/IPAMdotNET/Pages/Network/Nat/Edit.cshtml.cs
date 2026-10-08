using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
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

    [BindProperty]
    public LinkedLinesForm Source { get; set; } = new();

    [BindProperty]
    public LinkedLinesForm Destination { get; set; } = new();

    [BindProperty(Name = CustomFieldForm.Prefix)]
    public Dictionary<int, string?> Custom { get; set; } = [];

    public List<CustomFieldInput> CustomInputs { get; private set; } = [];

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
            Source = await LinkedLines.FormAsync(db, rule.Sources.Select(o => (o.Text, o.SubnetId, o.AddressId)));
            Destination = await LinkedLines.FormAsync(db, rule.Destinations.Select(o => (o.Text, o.SubnetId, o.AddressId)));
        }
        CustomInputs = await CustomFieldForm.LoadAsync(db, nameof(NatRule), id ?? 0);
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
            .. await ObjectsAsync(Source, NatSideKind.Source, "Source.Text", "Au moins une source est requise."),
            .. await ObjectsAsync(Destination, NatSideKind.Destination, "Destination.Text", "Au moins une destination est requise."),
        ];
        if (Rule.DeviceId is int deviceId && !await db.Devices.AnyAsync(d => d.Id == deviceId))
        {
            ModelState.AddModelError("Rule.DeviceId", "Équipement inexistant.");
        }
        List<CustomField> customFields = await CustomFieldForm.DefinitionsAsync(db, nameof(NatRule));
        Dictionary<int, string?> customValues = CustomFieldForm.Validate(customFields, Custom, ModelState);
        if (!ModelState.IsValid)
        {
            CustomInputs = CustomFieldForm.FromPosted(customFields, Custom);
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
        await CustomFieldForm.SaveAsync(db, rule.Id, customValues);
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

    private async Task<List<NatRuleObject>> ObjectsAsync(LinkedLinesForm form, NatSideKind kind, string field, string required)
    {
        List<NatSide> sides = await LinkedLines.ResolveAsync(db, form, ModelState, field, subnetsOnly: false);
        if (sides.Count == 0)
        {
            ModelState.AddModelError(field, required);
        }
        return sides.Select(s => new NatRuleObject { Side = kind, Text = s.Text, SubnetId = s.SubnetId, AddressId = s.AddressId }).ToList();
    }

    private async Task LoadAsync()
    {
        Devices = await db.Devices.OrderBy(d => d.Hostname).Select(d => new SelectListItem(d.Hostname, d.Id.ToString())).ToListAsync();
        await LinkedLines.LabelAsync(db, Source);
        await LinkedLines.LabelAsync(db, Destination);
    }
}
