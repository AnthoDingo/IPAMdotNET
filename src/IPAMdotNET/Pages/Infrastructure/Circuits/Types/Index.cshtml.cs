using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Infrastructure.Circuits.Types;

public sealed record CircuitTypeRow(CircuitType Type, int CircuitCount);

public class IndexModel(AppDbContext db) : PageModel
{
    public List<CircuitTypeRow> Types { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Types = await db.CircuitTypes
            .OrderBy(t => t.Name)
            .Select(t => new CircuitTypeRow(t, db.Circuits.Count(c => c.TypeId == t.Id)))
            .ToListAsync();
    }
}
