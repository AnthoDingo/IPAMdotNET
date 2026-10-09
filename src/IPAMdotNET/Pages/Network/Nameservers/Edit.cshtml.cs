using IPAMdotNet.Localization;
using System.Net;
using IPAMdotNet.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IPAMdotNet.Pages.Network.Nameservers;

[Authorize(Policy = "Admin")]
public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public Nameserver Nameserver { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is null)
        {
            return Page();
        }
        Nameserver? nameserver = await db.Nameservers.FindAsync(id);
        if (nameserver is null)
        {
            return NotFound();
        }
        Nameserver = nameserver;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Nameserver.Id = id ?? 0;

        // Séparateurs acceptés : « ; », « , », espaces et retours à la ligne. Stockage normalisé avec « ; ».
        string[] servers = Nameserver.Servers.Split([';', ',', ' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        List<string> normalized = [];
        foreach (string server in servers)
        {
            if (IPAddress.TryParse(server, out IPAddress? address))
            {
                normalized.Add(address.ToString());
            }
            else
            {
                ModelState.AddModelError("Nameserver.Servers", L.T("Adresse IP invalide : {0}", server));
            }
        }
        Nameserver.Servers = string.Join(';', normalized);

        if (!ModelState.IsValid)
        {
            return Page();
        }
        db.Update(Nameserver);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        Nameserver? nameserver = await db.Nameservers.FindAsync(id);
        if (nameserver is null)
        {
            return NotFound();
        }
        db.Nameservers.Remove(nameserver);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }
}
