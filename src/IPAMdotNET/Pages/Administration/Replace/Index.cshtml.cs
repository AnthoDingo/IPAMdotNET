using System.ComponentModel.DataAnnotations;
using System.Reflection;
using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace IPAMdotNet.Pages.Administration.Replace;

public sealed record Replacement(string Label, string Before, string After);

/// <summary>Rechercher / remplacer dans un champ texte d'un type d'objet (« Replace fields » de phpIPAM).</summary>
public class IndexModel(AppDbContext db) : PageModel
{
    /// <summary>Types concernés : les objets métier journalisés, hors comptes et données techniques.</summary>
    private static readonly string[] ExcludedTypes = [nameof(User), nameof(AppSetting), nameof(IpRequest), nameof(CustomField), nameof(NatRuleObject)];

    /// <summary>Champs normalisés à la saisie (adresses, préfixes, coordonnées) : un remplacement textuel les rendrait invalides.</summary>
    private static readonly HashSet<string> ExcludedFields =
    [
        "Device.IpAddress", "BgpPeer.LocalAddress", "BgpPeer.PeerAddress",
        "Nameserver.Servers", "PstnPrefix.Prefix", "Location.Latitude", "Location.Longitude",
    ];

    [BindProperty(SupportsGet = true), Display(Name = "Type d'objet")]
    public string? Type { get; set; }

    [BindProperty(SupportsGet = true), Display(Name = "Champ")]
    public string? Field { get; set; }

    [BindProperty(SupportsGet = true), Display(Name = "Rechercher")]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true), Display(Name = "Remplacer par")]
    public string? ReplaceWith { get; set; }

    [BindProperty(SupportsGet = true), Display(Name = "Respecter la casse")]
    public bool MatchCase { get; set; }

    public List<SelectListItem> Types { get; private set; } = [];
    public List<SelectListItem> Fields { get; private set; } = [];
    public List<Replacement>? Preview { get; private set; }

    [TempData]
    public string? Message { get; set; }

    public async Task OnGetAsync()
    {
        LoadLists();
        if (Field is not null && !string.IsNullOrEmpty(Search))
        {
            Preview = (await ComputeAsync()).Select(c => c.Replacement).ToList();
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        LoadLists();
        if (string.IsNullOrEmpty(Search) || Fields.All(f => f.Value != Field))
        {
            ModelState.AddModelError(string.Empty, "Choisissez un champ et le texte à rechercher.");
            return Page();
        }
        List<(object Entity, Replacement Replacement)> changes = await ComputeAsync();
        PropertyInfo property = EntityType()!.ClrType.GetProperty(Field!)!;

        // Validation de chaque objet modifié (longueur, champ obligatoire) avant tout enregistrement.
        List<string> errors = [];
        foreach ((object entity, Replacement replacement) in changes)
        {
            property.SetValue(entity, replacement.After.Length == 0 && IsNullable(property) ? null : replacement.After);
            List<ValidationResult> results = [];
            if (!Validator.TryValidateProperty(property.GetValue(entity), new ValidationContext(entity) { MemberName = property.Name }, results))
            {
                errors.Add($"{replacement.Label} : valeur refusée ({results[0].ErrorMessage}).");
            }
        }
        if (errors.Count > 0)
        {
            db.ChangeTracker.Clear();
            foreach (string error in errors.Take(20))
            {
                ModelState.AddModelError(string.Empty, error);
            }
            Preview = changes.Select(c => c.Replacement).ToList();
            return Page();
        }
        await db.SaveChangesAsync();
        string label = $"{ChangeLog.Types[Type!].Label} / {Fields.First(f => f.Value == Field).Text}";
        await db.LogAsync(LogSeverity.Info, LogEntry.Maintenance, $"Remplacement « {Search} » → « {ReplaceWith} » dans {label} : {changes.Count} objet(s).",
            User.Identity?.Name, HttpContext.Connection.RemoteIpAddress?.ToString());
        Message = $"{changes.Count} objet(s) modifié(s) ({label}).";
        return RedirectToPage(new { Type, Field });
    }

    private IEntityType? EntityType() =>
        Type is null || ExcludedTypes.Contains(Type) || !ChangeLog.Types.ContainsKey(Type) ? null
            : db.Model.GetEntityTypes().FirstOrDefault(e => e.ClrType.Name == Type);

    private void LoadLists()
    {
        Types = ChangeLog.Types.Where(t => !ExcludedTypes.Contains(t.Key))
            .OrderBy(t => t.Value.Label).Select(t => new SelectListItem(t.Value.Label, t.Key)).ToList();
        IEntityType? entityType = EntityType();
        Fields = entityType is null ? [] : entityType.GetProperties()
            .Where(p => p.ClrType == typeof(string) && p.PropertyInfo is not null && !ExcludedFields.Contains($"{Type}.{p.Name}"))
            .Select(p => new SelectListItem(p.PropertyInfo!.GetCustomAttribute<DisplayAttribute>()?.Name ?? p.Name, p.Name))
            .ToList();
    }

    /// <summary>Objets (suivis par EF) dont le champ contient le texte recherché, avec la valeur après remplacement.</summary>
    private async Task<List<(object Entity, Replacement Replacement)>> ComputeAsync()
    {
        IEntityType? entityType = EntityType();
        PropertyInfo? property = entityType?.ClrType.GetProperty(Field ?? "");
        if (entityType is null || property is null || Fields.All(f => f.Value != Field) || string.IsNullOrEmpty(Search))
        {
            return [];
        }
        // IQueryable<T> est covariant : la liste générique de n'importe quel type d'entité se lit comme IQueryable<object>.
        IQueryable<object> set = (IQueryable<object>)typeof(DbContext).GetMethod(nameof(DbContext.Set), System.Type.EmptyTypes)!
            .MakeGenericMethod(entityType.ClrType).Invoke(db, null)!;
        // ponytail: chargement complet du type en mémoire ; suffisant pour des volumes IPAM, filtrer en base sinon.
        List<object> entities = await set.ToListAsync();
        StringComparison comparison = MatchCase ? StringComparison.Ordinal : StringComparison.CurrentCultureIgnoreCase;
        List<(object, Replacement)> changes = [];
        foreach (object entity in entities)
        {
            if (property.GetValue(entity) is string before && before.Contains(Search, comparison))
            {
                string after = before.Replace(Search, ReplaceWith ?? "", comparison);
                changes.Add((entity, new Replacement(Label(entity), before, after)));
            }
        }
        return changes;
    }

    private static string Label(object entity)
    {
        if (entity is Subnet subnet)
        {
            return subnet.Network.ToString();
        }
        foreach (string name in new[] { "Name", "Hostname", "Cid", "Prefix" })
        {
            if (entity.GetType().GetProperty(name)?.GetValue(entity) is string value)
            {
                return value;
            }
        }
        return entity.GetType().GetProperty("Id")?.GetValue(entity)?.ToString() ?? "?";
    }

    private static bool IsNullable(PropertyInfo property) =>
        new NullabilityInfoContext().Create(property).WriteState == NullabilityState.Nullable;
}
