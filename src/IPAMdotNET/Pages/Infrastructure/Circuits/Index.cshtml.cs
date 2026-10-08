using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Infrastructure.Circuits;

public class IndexModel(AppDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true, Name = CustomFieldList.Prefix)]
    public CustomFieldList Custom { get; set; } = new();

    public List<Circuit> Circuits { get; private set; } = [];

    /// <summary>Équipements d'extrémité lisibles ; les autres sont affichés masqués.</summary>
    public HashSet<int> VisibleDevices { get; private set; } = [];

    /// <summary>Circuits logiques dont chaque circuit fait partie.</summary>
    public ILookup<int, LogicalCircuit> Logical { get; private set; } = Array.Empty<LogicalCircuit>().ToLookup(_ => 0);

    public async Task OnGetAsync()
    {
        await Custom.LoadAsync(db, nameof(Circuit));
        Circuits = await Custom.Apply(db, db.Circuits)
            .Include(c => c.Provider).Include(c => c.Type).Include(c => c.LocationA).Include(c => c.LocationB).Include(c => c.Customer)
            .Include(c => c.DeviceA).Include(c => c.DeviceB)
            .OrderBy(c => c.Provider!.Name).ThenBy(c => c.Cid)
            .ToListAsync();
        VisibleDevices = [.. await (await SectionAccess.ForAsync(db, User)).Readable(db.Devices).Select(d => d.Id).ToListAsync()];
        Logical = (await db.LogicalCircuitMembers.Include(m => m.LogicalCircuit).ToListAsync())
            .OrderBy(m => m.LogicalCircuit!.Cid).ToLookup(m => m.CircuitId, m => m.LogicalCircuit!);
    }
}
