using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages;

public class IndexModel(AppDbContext db) : PageModel
{
    public int SectionCount { get; private set; }
    public int SubnetCount { get; private set; }
    public int VlanCount { get; private set; }
    public int VrfCount { get; private set; }
    public int UserCount { get; private set; }

    public async Task OnGetAsync()
    {
        SectionCount = await db.Sections.CountAsync();
        SubnetCount = await db.Subnets.CountAsync();
        VlanCount = await db.Vlans.CountAsync();
        VrfCount = await db.Vrfs.CountAsync();
        UserCount = await db.Users.CountAsync();
    }
}
