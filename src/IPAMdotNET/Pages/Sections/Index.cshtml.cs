using IPAMdotNet.Data;
using IPAMdotNet.Networking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IPAMdotNet.Pages.Sections;

public class IndexModel(AppDbContext db) : PageModel
{
    public Section Section { get; private set; } = new();
    public List<SubnetNode> Tree { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Section? section = await db.Sections.FindAsync(id);
        if (section is null)
        {
            return NotFound();
        }
        Section = section;
        Tree = await SubnetTree.LoadAsync(db, id);
        return Page();
    }
}
