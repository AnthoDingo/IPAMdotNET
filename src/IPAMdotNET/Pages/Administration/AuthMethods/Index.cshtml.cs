using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Administration.AuthMethods;

public sealed record AuthMethodRow(AuthMethod Method, int UserCount);

public class IndexModel(AppDbContext db) : PageModel
{
    public List<AuthMethodRow> Methods { get; private set; } = [];
    public int LocalUserCount { get; private set; }

    public async Task OnGetAsync()
    {
        Methods = await db.AuthMethods.OrderBy(a => a.Name)
            .Select(a => new AuthMethodRow(a, db.Users.Count(u => u.AuthMethodId == a.Id)))
            .ToListAsync();
        LocalUserCount = await db.Users.CountAsync(u => u.AuthMethodId == null);
    }
}
