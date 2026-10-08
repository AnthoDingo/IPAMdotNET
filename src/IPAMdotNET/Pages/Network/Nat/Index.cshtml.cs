using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Network.Nat;

public class IndexModel(AppDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true, Name = CustomFieldList.Prefix)]
    public CustomFieldList Custom { get; set; } = new();

    public List<NatRule> Rules { get; private set; } = [];

    public async Task OnGetAsync()
    {
        await Custom.LoadAsync(db, nameof(NatRule));
        Rules = await Custom.Apply(db, db.NatRules).Include(n => n.Objects).ThenInclude(o => o.Subnet).Include(n => n.Objects).ThenInclude(o => o.Address)
            .Include(n => n.Device).OrderBy(n => n.Name).ToListAsync();
    }
}
