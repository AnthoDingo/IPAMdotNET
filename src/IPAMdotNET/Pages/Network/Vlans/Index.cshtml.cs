using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Network.Vlans;

public sealed record VlanRow(Vlan Vlan, int SubnetCount);

public class IndexModel(AppDbContext db) : PageModel
{
    public List<CustomField> CustomFields { get; private set; } = [];
    public Dictionary<int, Dictionary<int, string>> CustomValues { get; private set; } = [];

    public List<VlanRow> Vlans { get; private set; } = [];

    public async Task OnGetAsync()
    {
        CustomFields = await CustomFieldForm.DefinitionsAsync(db, nameof(Vlan));
        CustomValues = await CustomFieldForm.ValuesForAsync(db, nameof(Vlan));
        Vlans = await db.Vlans
            .OrderBy(v => v.Number)
            .Select(v => new VlanRow(v, db.Subnets.Count(s => s.VlanId == v.Id)))
            .ToListAsync();
    }
}
