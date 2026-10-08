using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Administration.Users;

public class IndexModel(AppDbContext db) : PageModel
{
    public List<User> Users { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Users = await db.Users.Include(u => u.AuthMethod).Include(u => u.Groups)
            .OrderBy(u => u.UserName).ToListAsync();
    }
}
