using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Infrastructure.Devices;

public class IndexModel(AppDbContext db) : PageModel
{
    public List<CustomField> CustomFields { get; private set; } = [];
    public Dictionary<int, Dictionary<int, string>> CustomValues { get; private set; } = [];

    public List<Device> Devices { get; private set; } = [];

    public async Task OnGetAsync()
    {
        CustomFields = await CustomFieldForm.DefinitionsAsync(db, nameof(Device));
        CustomValues = await CustomFieldForm.ValuesForAsync(db, nameof(Device));
        Devices = await db.Devices
            .Include(d => d.DeviceType).Include(d => d.Location).Include(d => d.Customer).Include(d => d.Rack)
            .OrderBy(d => d.Hostname)
            .ToListAsync();
    }
}
