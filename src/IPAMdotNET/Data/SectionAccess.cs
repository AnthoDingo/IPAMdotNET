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

    public static async Task<SectionAccess> ForAsync(AppDbContext db, ClaimsPrincipal user)
    {
        if (user.IsInRole("Admin"))
        {
            return new SectionAccess(true, []);
        }
        int userId = int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out int id) ? id : 0;
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

    /// <summary>Identifiants des sections lisibles ; null = toutes (admin).</summary>
    public IReadOnlyCollection<int>? ReadableIds => isAdmin ? null : levels.Where(l => l.Value >= SectionAccessLevel.Read).Select(l => l.Key).ToList();
}
