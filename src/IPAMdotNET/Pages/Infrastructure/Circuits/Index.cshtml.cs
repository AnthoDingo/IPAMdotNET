using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Infrastructure.Circuits;

public class IndexModel(AppDbContext db) : PageModel
{
    public List<CustomField> CustomFields { get; private set; } = [];
    public Dictionary<int, Dictionary<int, string>> CustomValues { get; private set; } = [];

    public List<Circuit> Circuits { get; private set; } = [];

    public async Task OnGetAsync()
    {
        CustomFields = await CustomFieldForm.DefinitionsAsync(db, nameof(Circuit));
        CustomValues = await CustomFieldForm.ValuesForAsync(db, nameof(Circuit));
        Circuits = await db.Circuits
            .Include(c => c.Provider).Include(c => c.LocationA).Include(c => c.LocationB).Include(c => c.Customer)
            .OrderBy(c => c.Provider!.Name).ThenBy(c => c.Cid)
            .ToListAsync();
    }
}
