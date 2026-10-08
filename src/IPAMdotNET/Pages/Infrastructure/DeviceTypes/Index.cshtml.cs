using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Infrastructure.DeviceTypes;

public sealed record DeviceTypeRow(DeviceType Type, int DeviceCount);

public class IndexModel(AppDbContext db) : PageModel
{
    public List<DeviceTypeRow> Types { get; private set; } = [];

    public async Task OnGetAsync()
    {
        IQueryable<Device> devices = (await SectionAccess.ForAsync(db, User)).Readable(db.Devices);
        Types = await db.DeviceTypes
            .OrderBy(t => t.Name)
            .Select(t => new DeviceTypeRow(t, devices.Count(d => d.DeviceTypeId == t.Id)))
            .ToListAsync();
    }
}
