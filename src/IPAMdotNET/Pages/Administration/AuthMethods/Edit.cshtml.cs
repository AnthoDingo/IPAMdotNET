using IPAMdotNet.Localization;
using System.ComponentModel.DataAnnotations;
using IPAMdotNet.Data;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Administration.AuthMethods;

public class EditModel(AppDbContext db) : PageModel
{
    [BindProperty]
    public AuthMethod Method { get; set; } = new();

    [BindProperty, Display(Name = "Utilisateur de test")]
    public string? TestUserName { get; set; }

    [BindProperty, DataType(DataType.Password), Display(Name = "Mot de passe de test")]
    public string? TestPassword { get; set; }

    public string? TestResult { get; private set; }
    public bool TestSucceeded { get; private set; }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is null)
        {
            return Page();
        }
        AuthMethod? method = await db.AuthMethods.FindAsync(id);
        if (method is null)
        {
            return NotFound();
        }
        Method = method;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Method.Id = id ?? 0;
        Validate();
        if (await db.AuthMethods.AnyAsync(a => a.Name == Method.Name && a.Id != Method.Id))
        {
            ModelState.AddModelError("Method.Name", L.T("Une méthode porte déjà ce nom."));
        }
        if (!ModelState.IsValid)
        {
            return Page();
        }
        db.Update(Method);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    /// <summary>Teste une liaison avec les valeurs saisies (même non enregistrées).</summary>
    public IActionResult OnPostTest(int? id)
    {
        Method.Id = id ?? 0;
        Validate();
        // Le test ne valide que la configuration et les identifiants de test.
        foreach (string key in ModelState.Keys.Where(k => k.StartsWith("Test", StringComparison.Ordinal)).ToList())
        {
            ModelState.Remove(key);
        }
        if (!ModelState.IsValid)
        {
            return Page();
        }
        LdapResult result = LdapAuthenticator.Verify(Method,
            string.IsNullOrWhiteSpace(TestUserName) ? "" : Data.User.NormalizeUserName(TestUserName), TestPassword ?? "", out string? error, out LdapUserInfo? entry);
        TestSucceeded = result == LdapResult.Success;
        TestResult = result switch
        {
            LdapResult.Success when string.IsNullOrWhiteSpace(Method.SearchBase) => L.T("Connexion réussie : l'annuaire a accepté ces identifiants."),
            LdapResult.Success when entry is null => L.T("Connexion réussie, mais le compte est introuvable avec cette base et ce filtre de recherche (pas de nom, d'e-mail ni de groupes)."),
            LdapResult.Success => L.T("Connexion réussie. Compte trouvé : {0}, {1} ; groupes : {2}.", entry!.DisplayName ?? L.T("(sans nom)"),
                entry.Email ?? L.T("(sans e-mail)"), entry.Groups.Count == 0 ? L.T("aucun") : string.Join(", ", entry.Groups)),
            LdapResult.InvalidCredentials => L.T("L'annuaire répond, mais refuse ces identifiants (ou le modèle d'identifiant ne correspond pas)."),
            _ => L.T("Annuaire injoignable : {0}", error),
        };
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        AuthMethod? method = await db.AuthMethods.FindAsync(id);
        if (method is null)
        {
            return NotFound();
        }
        if (await db.Users.AnyAsync(u => u.AuthMethodId == id))
        {
            Method = method;
            ModelState.AddModelError(string.Empty, L.T("Des utilisateurs utilisent cette méthode : rattachez-les à une autre méthode avant de la supprimer."));
            return Page();
        }
        db.AuthMethods.Remove(method);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }

    private void Validate()
    {
        if ((Method.AutoCreateUsers || Method.SyncGroups) && string.IsNullOrWhiteSpace(Method.SearchBase))
        {
            ModelState.AddModelError("Method.SearchBase", L.T("La création des comptes et la synchronisation des groupes lisent l'annuaire : indiquez la base de recherche."));
        }
        if (!string.IsNullOrWhiteSpace(Method.UserFilter) && !Method.UserFilter.Contains("{0}", StringComparison.Ordinal))
        {
            ModelState.AddModelError("Method.UserFilter", L.T("Le filtre doit contenir {0} (remplacé par le nom d'utilisateur)."));
        }
        if (!Method.BindTemplate.Contains("{0}", StringComparison.Ordinal))
        {
            ModelState.AddModelError("Method.BindTemplate", L.T("Le modèle doit contenir {0} (remplacé par le nom d'utilisateur)."));
        }
    }
}
