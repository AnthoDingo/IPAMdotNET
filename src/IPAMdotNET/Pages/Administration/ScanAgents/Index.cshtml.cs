using System.ComponentModel.DataAnnotations;
using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Administration.ScanAgents;

public class IndexModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public ScanSettings Settings { get; set; } = new();

    public List<Subnet> ScannedSubnets { get; private set; } = [];
    public List<RemoteAgent> Agents { get; private set; } = [];

    [BindProperty, Required(ErrorMessage = "Le nom est requis."), MaxLength(100), Display(Name = "Nom")]
    public string? AgentName { get; set; }

    [BindProperty, MaxLength(500), Display(Name = "Description")]
    public string? AgentDescription { get; set; }

    [TempData]
    public string? Message { get; set; }

    /// <summary>Clé en clair du nouvel agent, affichée une seule fois.</summary>
    [TempData]
    public string? CreatedKey { get; set; }

    public async Task OnGetAsync()
    {
        Settings = await SettingsStore.LoadAsync<ScanSettings>(db, SettingsStore.ScanPrefix);
        Agents = await db.RemoteAgents.OrderBy(a => a.Name).ToListAsync();
        ScannedSubnets = await db.Subnets.Include(s => s.Section).Include(s => s.ScanAgent)
            .Where(s => s.PingCheck || s.Discover)
            .OrderBy(s => s.Section!.Name).ThenBy(s => s.Address).ThenBy(s => s.PrefixLength)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Le formulaire des réglages ne porte pas les champs d'un nouvel agent.
        ModelState.Remove(nameof(AgentName));
        if (Settings.TcpPortList.Length != (Settings.TcpPorts ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Length)
        {
            ModelState.AddModelError("Settings.TcpPorts", "Ports entre 1 et 65535, sans doublon.");
        }
        if (!ModelState.IsValid)
        {
            await OnGetAsync();
            return Page();
        }
        await SettingsStore.SaveAsync(db, SettingsStore.ScanPrefix, Settings);
        await db.LogAsync(LogSeverity.Info, SubnetScanner.LogCategory,
            Settings.Enabled ? $"Agent de scan activé (toutes les {Settings.IntervalMinutes} min)." : "Agent de scan désactivé.",
            User.Identity?.Name, HttpContext.Connection.RemoteIpAddress?.ToString());
        Message = "Configuration enregistrée.";
        return RedirectToPage();
    }

    /// <summary>Cycle immédiat sur tous les sous-réseaux dont le scan est activé, sans attendre l'intervalle.</summary>
    public async Task<IActionResult> OnPostRunAsync()
    {
        if (!(await SettingsStore.LoadAsync<ScanSettings>(db, SettingsStore.ScanPrefix)).Enabled)
        {
            Message = "L'agent est désactivé : activez-le d'abord.";
            return RedirectToPage();
        }
        ScanAgent.Wake(force: true);
        Message = "Cycle lancé en arrière-plan : son résumé s'affichera ici une fois terminé.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCreateAgentAsync()
    {
        foreach (string key in ModelState.Keys.Where(k => k.StartsWith("Settings.")).ToList())
        {
            ModelState.Remove(key);
        }
        if (!ModelState.IsValid)
        {
            await OnGetAsync();
            return Page();
        }
        string secret = ApiKey.NewKey();
        db.RemoteAgents.Add(new RemoteAgent
        {
            Name = AgentName!.Trim(),
            Description = AgentDescription,
            KeyHash = ApiKey.Hash(secret),
            KeyPrefix = secret[..8],
        });
        await db.SaveChangesAsync();
        CreatedKey = secret;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAgentAsync(int id)
    {
        RemoteAgent? agent = await db.RemoteAgents.FindAsync(id);
        if (agent is not null)
        {
            agent.Enabled = !agent.Enabled;
            await db.SaveChangesAsync();
        }
        return RedirectToPage();
    }

    /// <summary>Ses sous-réseaux reviennent à l'agent intégré (clé étrangère SET NULL).</summary>
    public async Task<IActionResult> OnPostDeleteAgentAsync(int id)
    {
        RemoteAgent? agent = await db.RemoteAgents.FindAsync(id);
        if (agent is not null)
        {
            db.RemoteAgents.Remove(agent);
            await db.SaveChangesAsync();
            Message = $"Agent « {agent.Name} » supprimé ; ses sous-réseaux reviennent à l'agent intégré.";
        }
        return RedirectToPage();
    }
}
