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

    [TempData]
    public string? Message { get; set; }

    public async Task OnGetAsync()
    {
        Settings = await SettingsStore.LoadAsync<ScanSettings>(db, SettingsStore.ScanPrefix);
        ScannedSubnets = await db.Subnets.Include(s => s.Section)
            .Where(s => s.PingCheck || s.Discover)
            .OrderBy(s => s.Section!.Name).ThenBy(s => s.Address).ThenBy(s => s.PrefixLength)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
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
    public async Task<IActionResult> OnPostRunAsync(CancellationToken cancellationToken)
    {
        if (!(await SettingsStore.LoadAsync<ScanSettings>(db, SettingsStore.ScanPrefix)).Enabled)
        {
            Message = "L'agent est désactivé : activez-le d'abord.";
            return RedirectToPage();
        }
        await ScanAgent.RunAsync(db, force: true, cancellationToken);
        Message = $"Cycle terminé : {ScanAgent.LastCycleSummary}";
        return RedirectToPage();
    }
}
