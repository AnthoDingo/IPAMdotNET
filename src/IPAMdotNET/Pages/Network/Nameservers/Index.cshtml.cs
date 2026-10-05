using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Network.Nameservers;

public sealed record NameserverRow(Nameserver Nameserver, int SubnetCount);

public class IndexModel(AppDbContext db) : PageModel
{
    public List<NameserverRow> Nameservers { get; private set; } = [];

    public async Task OnGetAsync()
    {
        // Nombre de sous-réseaux limité aux sections lisibles.
        IQueryable<Subnet> readable = (await SectionAccess.ForAsync(db, User)).Readable(db.Subnets);
        Nameservers = await db.Nameservers
            .OrderBy(n => n.Name)
            .Select(n => new NameserverRow(n, readable.Count(s => s.NameserverId == n.Id)))
            .ToListAsync();
    }
}
