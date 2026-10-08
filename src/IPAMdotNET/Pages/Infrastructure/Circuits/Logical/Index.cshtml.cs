using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Infrastructure.Circuits.Logical;

public class IndexModel(AppDbContext db) : PageModel
{
    public List<LogicalCircuit> Circuits { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Circuits = await db.LogicalCircuits
            .Include(l => l.Members).ThenInclude(m => m.Circuit).ThenInclude(c => c!.Provider)
            .Include(l => l.Members).ThenInclude(m => m.Circuit).ThenInclude(c => c!.Type)
            .OrderBy(l => l.Cid).ToListAsync();
    }
}
