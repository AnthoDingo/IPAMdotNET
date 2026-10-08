using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Infrastructure.Pstn;

public class DetailsModel(AppDbContext db) : PageModel
{
    public PstnPrefix Prefix { get; private set; } = new();
    public List<PstnNumber> Numbers { get; private set; } = [];

    /// <summary>Numéros attribués (utilisation), indépendamment du filtre.</summary>
    public int NumberCount { get; private set; }

    [BindProperty(SupportsGet = true, Name = CustomFieldList.Prefix)]
    public CustomFieldList Custom { get; set; } = new();

    public List<CustomFieldInput> CustomFieldValues { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id)
    {
        PstnPrefix? prefix = await db.PstnPrefixes.Include(p => p.Device).SingleOrDefaultAsync(p => p.Id == id);
        if (prefix is null)
        {
            return NotFound();
        }
        Prefix = prefix;
        NumberCount = await db.PstnNumbers.CountAsync(n => n.PrefixId == id);
        CustomFieldValues = await CustomFieldForm.LoadAsync(db, nameof(PstnPrefix), id);
        await Custom.LoadAsync(db, nameof(PstnNumber));
        Numbers = await Custom.Apply(db, db.PstnNumbers).Include(n => n.Device).Where(n => n.PrefixId == id).OrderBy(n => n.Number).ToListAsync();
        return Page();
    }
}
