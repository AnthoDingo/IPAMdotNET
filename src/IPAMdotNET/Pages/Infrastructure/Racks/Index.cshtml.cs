using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Infrastructure.Racks;

public sealed record RackRow(Rack Rack, int DeviceCount, int UsedUnits);

public class IndexModel(AppDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true, Name = CustomFieldList.Prefix)]
    public CustomFieldList Custom { get; set; } = new();

    public List<RackRow> Racks { get; private set; } = [];

    public async Task OnGetAsync()
    {
        await Custom.LoadAsync(db, nameof(Rack));
        Racks = await Custom.Apply(db, db.Racks)
            .Include(r => r.Location).Include(r => r.Customer)
            .OrderBy(r => r.Name)
            .Select(r => new RackRow(r, r.Devices.Count, r.Devices.Sum(d => d.RackSize ?? 0)))
            .ToListAsync();
    }
}
