using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Infrastructure.Devices;

public class IndexModel(AppDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true, Name = CustomFieldList.Prefix)]
    public CustomFieldList Custom { get; set; } = new();

    public List<Device> Devices { get; private set; } = [];

    public async Task OnGetAsync()
    {
        await Custom.LoadAsync(db, nameof(Device));
        Devices = await Custom.Apply(db, (await SectionAccess.ForAsync(db, User)).Readable(db.Devices))
            .Include(d => d.DeviceType).Include(d => d.Location).Include(d => d.Customer).Include(d => d.Rack)
            .OrderBy(d => d.Hostname)
            .ToListAsync();
    }
}
