using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Network.Vrfs;

public sealed record VrfRow(Vrf Vrf, int SubnetCount);

public class IndexModel(AppDbContext db) : PageModel
{
    public List<CustomField> CustomFields { get; private set; } = [];
    public Dictionary<int, Dictionary<int, string>> CustomValues { get; private set; } = [];

    public List<VrfRow> Vrfs { get; private set; } = [];

    public async Task OnGetAsync()
    {
        CustomFields = await CustomFieldForm.DefinitionsAsync(db, nameof(Vrf));
        CustomValues = await CustomFieldForm.ValuesForAsync(db, nameof(Vrf));
        Vrfs = await db.Vrfs
            .OrderBy(v => v.Name)
            .Select(v => new VrfRow(v, db.Subnets.Count(s => s.VrfId == v.Id)))
            .ToListAsync();
    }
}
