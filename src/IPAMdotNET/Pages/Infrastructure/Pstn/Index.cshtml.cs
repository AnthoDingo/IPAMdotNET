using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Infrastructure.Pstn;

public sealed record PstnNode(PstnPrefix Prefix, int Depth, int NumberCount);

public class IndexModel(AppDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true, Name = CustomFieldList.Prefix)]
    public CustomFieldList Custom { get; set; } = new();

    public List<PstnNode> Nodes { get; private set; } = [];

    public async Task OnGetAsync()
    {
        await Custom.LoadAsync(db, nameof(PstnPrefix));
        List<PstnPrefix> prefixes = await Custom.Apply(db, db.PstnPrefixes).Include(p => p.Device).ToListAsync();
        Dictionary<int, int> counts = await db.PstnNumbers.GroupBy(n => n.PrefixId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        // Tri ordinal : un préfixe précède tous ceux qu'il commence (+33 < +331 < +3312 < +332).
        Stack<PstnPrefix> ancestors = new();
        foreach (PstnPrefix prefix in prefixes.OrderBy(p => p.Prefix, StringComparer.Ordinal))
        {
            while (ancestors.Count > 0 && !prefix.Prefix.StartsWith(ancestors.Peek().Prefix, StringComparison.Ordinal))
            {
                ancestors.Pop();
            }
            Nodes.Add(new PstnNode(prefix, ancestors.Count, counts.GetValueOrDefault(prefix.Id)));
            ancestors.Push(prefix);
        }
    }
}
