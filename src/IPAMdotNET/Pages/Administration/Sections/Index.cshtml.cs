using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Administration.Sections;

public sealed record SectionRow(Section Section, int SubnetCount);

public class IndexModel(AppDbContext db) : PageModel
{
    public List<SectionRow> Sections { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Sections = await db.Sections
            .OrderBy(s => s.Name)
            .Select(s => new SectionRow(s, s.Subnets.Count))
            .ToListAsync();
    }
}
