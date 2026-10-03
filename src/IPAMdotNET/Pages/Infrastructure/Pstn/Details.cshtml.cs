using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Infrastructure.Pstn;

public class DetailsModel(AppDbContext db) : PageModel
{
    public PstnPrefix Prefix { get; private set; } = new();
    public List<PstnNumber> Numbers { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id)
    {
        PstnPrefix? prefix = await db.PstnPrefixes.Include(p => p.Device).SingleOrDefaultAsync(p => p.Id == id);
        if (prefix is null)
        {
            return NotFound();
        }
        Prefix = prefix;
        Numbers = await db.PstnNumbers.Include(n => n.Device).Where(n => n.PrefixId == id).OrderBy(n => n.Number).ToListAsync();
        return Page();
    }
}
