using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Network.Vrfs;

public sealed record VrfRow(Vrf Vrf, int SubnetCount);

public class IndexModel(AppDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true, Name = CustomFieldList.Prefix)]
    public CustomFieldList Custom { get; set; } = new();

    public List<VrfRow> Vrfs { get; private set; } = [];

    public async Task OnGetAsync()
    {
        // Nombre de sous-réseaux limité aux sections lisibles.
        IQueryable<Subnet> readable = (await SectionAccess.ForAsync(db, User)).Readable(db.Subnets);
        await Custom.LoadAsync(db, nameof(Vrf));
        Vrfs = await Custom.Apply(db, db.Vrfs)
            .OrderBy(v => v.Name)
            .Select(v => new VrfRow(v, readable.Count(s => s.VrfId == v.Id)))
            .ToListAsync();
    }
}
