using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Administration.Logs;

public class IndexModel(AppDbContext db) : PageModel
{
    public const int PageSize = 50;

    [BindProperty(SupportsGet = true)]
    public LogSeverity? Severity { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Category { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true, Name = "p")]
    public int PageNumber { get; set; } = 1;

    public List<LogEntry> Entries { get; private set; } = [];
    public List<string> Categories { get; private set; } = [];
    public int TotalCount { get; private set; }
    public int PageCount => Math.Max(1, (TotalCount + PageSize - 1) / PageSize);

    public async Task OnGetAsync()
    {
        Categories = await db.LogEntries.Select(l => l.Category).Distinct().OrderBy(c => c).ToListAsync();
        IQueryable<LogEntry> query = db.LogEntries;
        if (Severity is not null)
        {
            query = query.Where(l => l.Severity == Severity);
        }
        if (!string.IsNullOrEmpty(Category))
        {
            query = query.Where(l => l.Category == Category);
        }
        if (!string.IsNullOrWhiteSpace(Q))
        {
            string text = Q.Trim().ToLowerInvariant();
            query = query.Where(l => l.Message.ToLower().Contains(text)
                || (l.UserName != null && l.UserName.ToLower().Contains(text)) || (l.IpAddress != null && l.IpAddress.Contains(text)));
        }
        TotalCount = await query.CountAsync();
        PageNumber = Math.Clamp(PageNumber, 1, PageCount);
        Entries = await query.OrderByDescending(l => l.Date).ThenByDescending(l => l.Id)
            .Skip((PageNumber - 1) * PageSize).Take(PageSize).ToListAsync();
    }
}
