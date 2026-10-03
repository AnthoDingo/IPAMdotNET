using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Infrastructure.Circuits.Providers;

public sealed record ProviderRow(CircuitProvider Provider, int CircuitCount);

public class IndexModel(AppDbContext db) : PageModel
{
    public List<ProviderRow> Providers { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Providers = await db.CircuitProviders
            .OrderBy(p => p.Name)
            .Select(p => new ProviderRow(p, db.Circuits.Count(c => c.ProviderId == p.Id)))
            .ToListAsync();
    }
}
