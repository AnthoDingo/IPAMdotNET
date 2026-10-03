using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Infrastructure.Circuits;

public class IndexModel(AppDbContext db) : PageModel
{
    public List<Circuit> Circuits { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Circuits = await db.Circuits
            .Include(c => c.Provider).Include(c => c.LocationA).Include(c => c.LocationB).Include(c => c.Customer)
            .OrderBy(c => c.Provider!.Name).ThenBy(c => c.Cid)
            .ToListAsync();
    }
}
