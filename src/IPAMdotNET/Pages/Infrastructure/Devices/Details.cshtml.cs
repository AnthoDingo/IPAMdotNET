using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Infrastructure.Devices;

public class DetailsModel(AppDbContext db) : PageModel
{
    public Device Device { get; private set; } = new();

    public List<CustomFieldInput> CustomFieldValues { get; private set; } = [];

    /// <summary>Adresses rattachées, limitées aux sous-réseaux des sections lisibles.</summary>
    public List<IpAddress> Addresses { get; private set; } = [];

    public List<NatRule> NatRules { get; private set; } = [];
    public List<PstnPrefix> PstnPrefixes { get; private set; } = [];
    public List<Circuit> Circuits { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Device? device = await db.Devices
            .Include(d => d.DeviceType).Include(d => d.Location).Include(d => d.Customer).Include(d => d.Rack).Include(d => d.Sections)
            .SingleOrDefaultAsync(d => d.Id == id);
        if (device is null)
        {
            return NotFound();
        }
        SectionAccess access = await SectionAccess.ForAsync(db, User);
        if (!access.CanSee(device))
        {
            return Forbid();
        }
        Device = device;
        IQueryable<Subnet> readable = access.Readable(db.Subnets);
        Addresses = await db.IpAddresses.Include(a => a.Subnet).Include(a => a.Tag)
            .Where(a => a.DeviceId == id && readable.Any(s => s.Id == a.SubnetId))
            .OrderBy(a => a.Address).ToListAsync();
        NatRules = await db.NatRules.Where(n => n.DeviceId == id).OrderBy(n => n.Name).ToListAsync();
        Circuits = await db.Circuits.Include(c => c.Provider).Where(c => c.DeviceAId == id || c.DeviceBId == id).OrderBy(c => c.Cid).ToListAsync();
        PstnPrefixes = await db.PstnPrefixes.Where(p => p.DeviceId == id).OrderBy(p => p.Prefix).ToListAsync();
        CustomFieldValues = await CustomFieldForm.LoadAsync(db, nameof(Device), id);
        return Page();
    }
}
