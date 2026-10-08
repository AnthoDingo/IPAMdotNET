using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Administration.Groups;

/// <summary>Groupe : membres et permission sur chaque section.</summary>
public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public Group Group { get; set; } = new();

    [BindProperty(Name = nameof(MemberIds))]
    public List<int> MemberIds { get; set; } = [];

    /// <summary>Niveau par section : SectionId → niveau (absent = aucun accès).</summary>
    // Nom explicite : sans lui, si aucune section n'est postée, ASP.NET retombe sur le préfixe vide (voir CustomFieldForm).
    [BindProperty(Name = nameof(Permissions))]
    public Dictionary<int, SectionAccessLevel> Permissions { get; set; } = [];

    public List<User> Users { get; private set; } = [];
    public List<Section> Sections { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is not null)
        {
            Group? group = await db.Groups.Include(g => g.Users).SingleOrDefaultAsync(g => g.Id == id);
            if (group is null)
            {
                return NotFound();
            }
            Group = group;
            MemberIds = group.Users.Select(u => u.Id).ToList();
            Permissions = await db.SectionPermissions.Where(p => p.GroupId == group.Id).ToDictionaryAsync(p => p.SectionId, p => p.Level);
        }
        await LoadListsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Group? group = id is null ? new Group() : await db.Groups.Include(g => g.Users).SingleOrDefaultAsync(g => g.Id == id);
        if (group is null)
        {
            return NotFound();
        }
        if (await db.Groups.AnyAsync(g => g.Name == Group.Name && g.Id != (id ?? 0)))
        {
            ModelState.AddModelError("Group.Name", "Un groupe porte déjà ce nom.");
        }
        if (!ModelState.IsValid)
        {
            Group.Id = id ?? 0;
            await LoadListsAsync();
            return Page();
        }
        group.Name = Group.Name.Trim();
        group.Description = Group.Description;
        group.Users = await db.Users.Where(u => MemberIds.Contains(u.Id)).ToListAsync();
        if (group.Id == 0)
        {
            db.Groups.Add(group);
        }
        await db.SaveChangesAsync();

        // Permissions : remplacées en bloc, seuls les niveaux autres que « aucun accès » sont conservés.
        await db.SectionPermissions.Where(p => p.GroupId == group.Id).ExecuteDeleteAsync();
        HashSet<int> sections = (await db.Sections.Select(s => s.Id).ToListAsync()).ToHashSet();
        db.SectionPermissions.AddRange(Permissions
            .Where(p => p.Value != SectionAccessLevel.None && sections.Contains(p.Key))
            .Select(p => new SectionPermission { GroupId = group.Id, SectionId = p.Key, Level = p.Value }));
        await db.SaveChangesAsync();
        await db.LogAsync(LogSeverity.Info, LogEntry.Maintenance,
            $"Groupe « {group.Name} » : {group.Users.Count} membre(s), permissions sur {Permissions.Count(p => p.Value != SectionAccessLevel.None)} section(s).",
            User.Identity?.Name, HttpContext.Connection.RemoteIpAddress?.ToString());
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        Group? group = await db.Groups.FindAsync(id);
        if (group is null)
        {
            return NotFound();
        }
        db.Groups.Remove(group);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    private async Task LoadListsAsync()
    {
        Users = await db.Users.Where(u => !u.IsAdmin).OrderBy(u => u.UserName).ToListAsync();
        Sections = await db.Sections.OrderBy(s => s.Name).ToListAsync();
    }
}
