using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Administration.Sections;

public sealed record SectionRow(Section Section, int SubnetCount);

public class IndexModel(AppDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true, Name = CustomFieldList.Prefix)]
    public CustomFieldList Custom { get; set; } = new();

    public List<SectionRow> Sections { get; private set; } = [];

    public async Task OnGetAsync()
    {
        await Custom.LoadAsync(db, nameof(Section));
        Sections = await Custom.Apply(db, db.Sections)
            .OrderBy(s => s.Name)
            .Select(s => new SectionRow(s, s.Subnets.Count))
            .ToListAsync();
    }
}
