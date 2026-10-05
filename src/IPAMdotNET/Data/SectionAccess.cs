using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Data;

/// <summary>
/// Droits d'un utilisateur sur les sections : le plus élevé entre l'accès par défaut de la section et les permissions
/// de ses groupes. Un admin a tous les droits.
/// </summary>
public sealed class SectionAccess
{
    private readonly bool isAdmin;
    private readonly Dictionary<int, SectionAccessLevel> levels;

    private SectionAccess(bool isAdmin, Dictionary<int, SectionAccessLevel> levels)
    {
        this.isAdmin = isAdmin;
        this.levels = levels;
    }

    public static Task<SectionAccess> ForAsync(AppDbContext db, ClaimsPrincipal user) =>
        user.IsInRole("Admin") ? Task.FromResult(new SectionAccess(true, []))
            : ForUserAsync(db, int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out int id) ? id : 0);

    /// <summary>Droits d'une clé d'API : ceux de son compte, ou de tout si elle n'en a pas.</summary>
    public static async Task<SectionAccess> ForApiKeyAsync(AppDbContext db, ApiKey key)
    {
        if (key.UserId is not int userId || await db.Users.AnyAsync(u => u.Id == userId && u.IsAdmin))
        {
            return new SectionAccess(true, []);
        }
        // Compte désactivé : la clé ne voit plus rien.
        return await db.Users.AnyAsync(u => u.Id == userId && u.Enabled) ? await ForUserAsync(db, userId) : new SectionAccess(false, []);
    }

    private static async Task<SectionAccess> ForUserAsync(AppDbContext db, int userId)
    {
        Dictionary<int, SectionAccessLevel> levels = await db.Sections.ToDictionaryAsync(s => s.Id, s => s.DefaultAccess);
        List<SectionPermission> permissions = await db.SectionPermissions
            .Where(p => db.Users.Where(u => u.Id == userId).SelectMany(u => u.Groups).Any(g => g.Id == p.GroupId))
            .ToListAsync();
        foreach (SectionPermission permission in permissions)
        {
            if (levels.TryGetValue(permission.SectionId, out SectionAccessLevel current) && permission.Level > current)
            {
                levels[permission.SectionId] = permission.Level;
            }
        }
        return new SectionAccess(false, levels);
    }

    public bool CanRead(int sectionId) => isAdmin || levels.GetValueOrDefault(sectionId) >= SectionAccessLevel.Read;

    public bool CanWrite(int sectionId) => isAdmin || levels.GetValueOrDefault(sectionId) >= SectionAccessLevel.Write;

    /// <summary>Peut créer des sous-réseaux dans au moins une section (bouton « Ajouter »).</summary>
    public bool CanWriteAny => isAdmin || levels.Values.Any(l => l >= SectionAccessLevel.Write);

    /// <summary>Restreint une requête de sous-réseaux aux sections lisibles (filtre traduit en SQL).</summary>
    public IQueryable<Subnet> Readable(IQueryable<Subnet> subnets) =>
        ReadableIds is { } ids ? subnets.Where(s => ids.Contains(s.SectionId)) : subnets;

    /// <summary>
    /// Journal des modifications visible : tout pour un admin ; sinon ni les objets d'administration, ni les objets
    /// rattachés à une section non lisible (ou inconnue).
    /// </summary>
    public IQueryable<ChangeLog> Visible(IQueryable<ChangeLog> logs)
    {
        if (ReadableIds is not { } ids)
        {
            return logs;
        }
        string[] adminOnly = ChangeLog.AdminOnlyTypes;
        string[] scoped = ChangeLog.SectionScopedTypes;
        return logs.Where(c => !adminOnly.Contains(c.EntityType)
            && (!scoped.Contains(c.EntityType) || (c.SectionId != null && ids.Contains(c.SectionId.Value))));
    }

    public bool IsAdmin => isAdmin;

    /// <summary>Équipements visibles : ceux sans section, ou rattachés à une section lisible.</summary>
    public IQueryable<Device> Readable(IQueryable<Device> devices) =>
        ReadableIds is { } ids ? devices.Where(d => !d.Sections.Any() || d.Sections.Any(s => ids.Contains(s.Id))) : devices;

    public bool CanSee(Device device) => ReadableIds is not { } ids || device.Sections.Count == 0 || device.Sections.Any(s => ids.Contains(s.Id));

    /// <summary>Identifiants des sections lisibles ; null = toutes (admin).</summary>
    public IReadOnlyCollection<int>? ReadableIds => isAdmin ? null : levels.Where(l => l.Value >= SectionAccessLevel.Read).Select(l => l.Key).ToList();
}
