using System.Text.Json;
using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Navigation;

public sealed record HistoryModel(string EntityType, int EntityId, List<(ChangeLog Log, Dictionary<string, string?[]> Changes)> Entries, int Total);

/// <summary>
/// Historique d'un objet sur sa fiche (comme phpIPAM) : dernières entrées du journal, filtrées par les droits.
/// Usage : <c>&lt;vc:history entity-type="@nameof(Subnet)" entity-id="@Model.Subnet.Id" /&gt;</c>.
/// </summary>
public sealed class HistoryViewComponent(AppDbContext db) : ViewComponent
{
    public const int Limit = 20;

    public async Task<IViewComponentResult> InvokeAsync(string entityType, int entityId)
    {
        SectionAccess access = await SectionAccess.ForAsync(db, HttpContext.User);
        IQueryable<ChangeLog> query = access.Visible(db.ChangeLogs).Where(c => c.EntityType == entityType && c.EntityId == entityId);
        int total = await query.CountAsync();
        List<ChangeLog> logs = await query.OrderByDescending(c => c.Date).ThenByDescending(c => c.Id).Take(Limit).ToListAsync();
        return View(new HistoryModel(entityType, entityId,
            logs.Select(l => (l, l.Changes is null ? [] : JsonSerializer.Deserialize<Dictionary<string, string?[]>>(l.Changes) ?? [])).ToList(), total));
    }
}
