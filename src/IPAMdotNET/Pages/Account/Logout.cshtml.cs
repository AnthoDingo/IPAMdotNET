using IPAMdotNet.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IPAMdotNet.Pages.Account;

public class LogoutModel(AppDbContext db) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            await db.TryLogAsync(LogSeverity.Info, LogEntry.Authentication, "Déconnexion.", User.Identity.Name,
                HttpContext.Connection.RemoteIpAddress?.ToString());
        }
        await HttpContext.SignOutAsync();
        return RedirectToPage("/Account/Login");
    }
}
