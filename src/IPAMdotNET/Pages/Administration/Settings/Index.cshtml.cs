using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IPAMdotNet.Pages.Administration.Settings;

public class IndexModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public ServerSettings Settings { get; set; } = new();

    [TempData]
    public string? Message { get; set; }

    public async Task OnGetAsync()
    {
        Settings = await SettingsStore.LoadAsync<ServerSettings>(db, SettingsStore.ServerPrefix);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }
        await SettingsStore.SaveAsync(db, SettingsStore.ServerPrefix, Settings);
        Message = "Paramètres enregistrés.";
        return RedirectToPage();
    }
}
