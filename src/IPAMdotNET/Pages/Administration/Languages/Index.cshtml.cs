using IPAMdotNet.Data;
using IPAMdotNet.Localization;
using IPAMdotNet.Maintenance;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Pages.Administration.Languages;

/// <param name="Coverage">Part des textes de référence (en-GB) traduits dans la langue ; null pour la langue source.</param>
/// <param name="Users">Comptes qui ont choisi cette langue (les autres suivent la langue par défaut du serveur).</param>
public sealed record LanguageRow(string Code, string Name, double? Coverage, int Users, bool IsDefault);

/// <summary>Langues de l'interface (catalogues Localization/i18n, compilés avec l'application) : avancement et utilisation.</summary>
public class IndexModel(AppDbContext db) : PageModel
{
    public List<LanguageRow> Languages { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Dictionary<string, int> users = await db.Users.Where(u => u.Language != null)
            .GroupBy(u => u.Language!).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        // Référence : le catalogue anglais, repli de toutes les autres langues. Un texte absent d'un catalogue s'affiche via ce repli.
        IReadOnlyCollection<string> reference = L.Catalog(L.Fallback).Keys.ToList();
        string defaultLanguage = SettingsStore.Server.Language;
        Languages = L.Languages.Select(l => new LanguageRow(l.Code, l.Name,
                l.Code == L.Source || reference.Count == 0 ? null
                : (double)reference.Count(key => IsTranslated(l.Code, key)) / reference.Count,
                users.GetValueOrDefault(l.Code), l.Code == defaultLanguage))
            .ToList();
    }

    /// <summary>Une variante de l'anglais (en-US) ne contient que ses différences : le texte anglais de référence vaut traduction.</summary>
    private static bool IsTranslated(string code, string key) =>
        L.Catalog(code).ContainsKey(key) || code[..2] == L.Fallback[..2] && L.Catalog(L.Fallback).ContainsKey(key);
}
