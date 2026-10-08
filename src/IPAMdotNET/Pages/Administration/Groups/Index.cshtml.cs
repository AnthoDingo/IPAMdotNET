using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Administration.Groups;

public sealed record GroupRow(Group Group, int MemberCount, int PermissionCount);

public class IndexModel(AppDbContext db) : PageModel
{
    public List<GroupRow> Groups { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Groups = await db.Groups.OrderBy(g => g.Name)
            .Select(g => new GroupRow(g, g.Users.Count, db.SectionPermissions.Count(p => p.GroupId == g.Id && p.Level != SectionAccessLevel.None)))
            .ToListAsync();
    }
}
