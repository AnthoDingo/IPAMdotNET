using System.Text.Json;
using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Tools.Changelog;

public sealed record ChangeRow(ChangeLog Log, Dictionary<string, string?[]> Changes);

public class IndexModel(AppDbContext db) : PageModel
{
    public const int PageSize = 50;

    [BindProperty(SupportsGet = true)]
    public string? Type { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    /// <summary>Historique d'un seul objet (avec <see cref="Type"/>).</summary>
    [BindProperty(SupportsGet = true)]
    public int? Id { get; set; }

    [BindProperty(SupportsGet = true, Name = "p")]
    public int PageNumber { get; set; } = 1;

    public List<ChangeRow> Rows { get; private set; } = [];
    public int TotalCount { get; private set; }
    public int PageCount => Math.Max(1, (TotalCount + PageSize - 1) / PageSize);

    public List<SelectListItem> Types { get; private set; } = [];

    /// <summary>Entrées filtrées selon les droits (<see cref="SectionAccess.Visible"/>).</summary>
    public async Task OnGetAsync()
    {
        SectionAccess access = await SectionAccess.ForAsync(db, User);
        Types = ChangeLog.Types
            .Where(t => access.IsAdmin || !ChangeLog.AdminOnlyTypes.Contains(t.Key))
            .OrderBy(t => t.Value.Label)
            .Select(t => new SelectListItem(t.Value.Label, t.Key))
            .ToList();
        IQueryable<ChangeLog> query = access.Visible(db.ChangeLogs);
        if (!string.IsNullOrEmpty(Type))
        {
            query = query.Where(c => c.EntityType == Type);
            if (Id is not null)
            {
                query = query.Where(c => c.EntityId == Id);
            }
        }
        if (!string.IsNullOrWhiteSpace(Q))
        {
            string text = Q.Trim().ToLowerInvariant();
            query = query.Where(c => c.UserName.ToLower().Contains(text) || (c.EntityLabel != null && c.EntityLabel.ToLower().Contains(text)));
        }
        TotalCount = await query.CountAsync();
        PageNumber = Math.Clamp(PageNumber, 1, PageCount);
        List<ChangeLog> logs = await query.OrderByDescending(c => c.Date).ThenByDescending(c => c.Id)
            .Skip((PageNumber - 1) * PageSize).Take(PageSize).ToListAsync();
        Rows = logs.Select(l => new ChangeRow(l, l.Changes is null ? []
            : JsonSerializer.Deserialize<Dictionary<string, string?[]>>(l.Changes) ?? [])).ToList();
    }
}
