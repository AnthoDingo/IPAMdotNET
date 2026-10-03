using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Infrastructure.Devices;

public class IndexModel(AppDbContext db) : PageModel
{
    public List<Device> Devices { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Devices = await db.Devices
            .Include(d => d.DeviceType).Include(d => d.Location).Include(d => d.Customer).Include(d => d.Rack)
            .OrderBy(d => d.Hostname)
            .ToListAsync();
    }
}
