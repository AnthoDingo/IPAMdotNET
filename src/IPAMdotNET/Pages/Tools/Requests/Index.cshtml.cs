using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using IPAMdotNet.Navigation;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Tools.Requests;

[IpRequestsEnabled]
public class IndexModel(AppDbContext db) : PageModel
{
    public List<IpRequest> Pending { get; private set; } = [];
    public List<IpRequest> Processed { get; private set; } = [];

    /// <summary>Un admin voit toutes les demandes, un utilisateur seulement les siennes.</summary>
    public async Task OnGetAsync()
    {
        IQueryable<IpRequest> query = db.IpRequests
            .Include(r => r.Subnet).ThenInclude(s => s!.Section)
            .Include(r => r.RequestedBy).Include(r => r.ProcessedBy);
        if (!User.IsInRole("Admin"))
        {
            int userId = User.UserId();
            query = query.Where(r => r.RequestedById == userId);
        }
        Pending = await query.Where(r => r.State == IpRequestState.Pending).OrderBy(r => r.RequestedAt).ToListAsync();
        Processed = await query.Where(r => r.State != IpRequestState.Pending).OrderByDescending(r => r.ProcessedAt).Take(100).ToListAsync();
    }
}
