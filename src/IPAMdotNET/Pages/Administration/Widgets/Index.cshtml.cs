using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IPAMdotNet.Pages.Administration.Widgets;

public class IndexModel(AppDbContext db) : PageModel
{
    [BindProperty(Name = nameof(Visible))]
    public List<string> Visible { get; set; } = [];

    [TempData]
    public string? Message { get; set; }

    public async Task OnGetAsync()
    {
        WidgetSettings settings = await SettingsStore.LoadAsync<WidgetSettings>(db, SettingsStore.WidgetsPrefix);
        Visible = WidgetSettings.All.Where(w => settings.IsVisible(w.Key)).Select(w => w.Key).ToList();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        WidgetSettings settings = new()
        {
            Hidden = string.Join(',', WidgetSettings.All.Select(w => w.Key).Where(key => !Visible.Contains(key))),
        };
        await SettingsStore.SaveAsync(db, SettingsStore.WidgetsPrefix, settings);
        Message = "Widgets enregistrés.";
        return RedirectToPage();
    }
}
