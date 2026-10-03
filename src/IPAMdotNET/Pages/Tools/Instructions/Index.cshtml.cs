using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IPAMdotNet.Pages.Tools.Instructions;

public class IndexModel(AppDbContext db) : PageModel
{
    public string? Text { get; private set; }

    public async Task OnGetAsync()
    {
        Text = (await db.AppSettings.FindAsync(AppSetting.InstructionsKey))?.Value;
    }
}
