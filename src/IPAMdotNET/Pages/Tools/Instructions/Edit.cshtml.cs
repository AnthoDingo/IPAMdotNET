using System.ComponentModel.DataAnnotations;
using IPAMdotNet.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IPAMdotNet.Pages.Tools.Instructions;

[Authorize(Policy = "Admin")]
public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty, MaxLength(20000), Display(Name = "Texte des instructions")]
    public string? Text { get; set; }

    /// <summary>Rendu de l'aperçu (texte non enregistré).</summary>
    public string? Preview { get; private set; }

    public IActionResult OnPostPreview()
    {
        Preview = Maintenance.MarkdownText.ToHtml(Text);
        return Page();
    }

    public async Task OnGetAsync()
    {
        Text = (await db.AppSettings.FindAsync(AppSetting.InstructionsKey))?.Value;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }
        AppSetting? setting = await db.AppSettings.FindAsync(AppSetting.InstructionsKey);
        if (setting is null)
        {
            db.AppSettings.Add(new AppSetting { Key = AppSetting.InstructionsKey, Value = Text });
        }
        else
        {
            setting.Value = Text;
        }
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }
}
