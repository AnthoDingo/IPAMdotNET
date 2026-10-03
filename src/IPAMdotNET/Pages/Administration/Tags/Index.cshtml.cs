using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Administration.Tags;

public class IndexModel(AppDbContext db) : PageModel
{
    public List<Tag> Tags { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Tags = await db.Tags.OrderByDescending(t => t.Locked).ThenBy(t => t.Name).ToListAsync();
    }
}
