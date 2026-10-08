using IPAMdotNet.Data;
using IPAMdotNet.Navigation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Tools.Favorites;

public class IndexModel(AppDbContext db) : PageModel
{
    public List<Subnet> Subnets { get; private set; } = [];

    public async Task OnGetAsync()
    {
        int userId = User.UserId();
        SectionAccess access = await SectionAccess.ForAsync(db, User);
        Subnets = await access.Readable(db.Subnets)
            .Include(s => s.Section).Include(s => s.Vlan).Include(s => s.Vrf)
            .Where(s => db.FavoriteSubnets.Any(f => f.UserId == userId && f.SubnetId == s.Id))
            .OrderBy(s => s.Address).ThenBy(s => s.PrefixLength)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostRemoveAsync(int subnetId)
    {
        int userId = User.UserId();
        await db.FavoriteSubnets.Where(f => f.UserId == userId && f.SubnetId == subnetId).ExecuteDeleteAsync();
        return RedirectToPage();
    }
}
