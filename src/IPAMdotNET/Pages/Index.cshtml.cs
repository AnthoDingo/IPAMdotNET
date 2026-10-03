using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages;

public class IndexModel(AppDbContext db) : PageModel
{
    public int UserCount { get; private set; }

    public async Task OnGetAsync()
    {
        UserCount = await db.Users.CountAsync();
    }
}
