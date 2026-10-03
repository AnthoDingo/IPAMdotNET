using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Administration.CustomFields;

public sealed record CustomFieldRow(CustomField Field, int ValueCount);

public class IndexModel(AppDbContext db) : PageModel
{
    public List<CustomFieldRow> Fields { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Fields = await db.CustomFields
            .OrderBy(f => f.EntityType).ThenBy(f => f.Order).ThenBy(f => f.Name)
            .Select(f => new CustomFieldRow(f, db.CustomFieldValues.Count(v => v.FieldId == f.Id)))
            .ToListAsync();
    }
}
