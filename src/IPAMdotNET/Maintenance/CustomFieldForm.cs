using System.Globalization;
using IPAMdotNet.Data;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Maintenance;

/// <summary>Un champ personnalisé et sa valeur pour un objet donné (formulaires et fiches).</summary>
public sealed record CustomFieldInput(CustomField Field, string? Value);

/// <summary>
/// Champs personnalisés dans les pages de modification. Les valeurs sont postées sous le nom « Custom[idDuChamp] »
/// et liées à un <c>Dictionary&lt;int, string?&gt;</c> de la page.
/// </summary>
public static class CustomFieldForm
{
    public const string Prefix = "Custom";

    public static Task<List<CustomField>> DefinitionsAsync(AppDbContext db, string entityType) =>
        db.CustomFields.Where(f => f.EntityType == entityType).OrderBy(f => f.Order).ThenBy(f => f.Name).ToListAsync();

    /// <summary>Définitions et valeurs enregistrées d'un objet (Id 0 = création : valeurs vides).</summary>
    public static async Task<List<CustomFieldInput>> LoadAsync(AppDbContext db, string entityType, int entityId)
    {
        List<CustomField> fields = await DefinitionsAsync(db, entityType);
        Dictionary<int, string> values = entityId == 0 ? [] : await db.CustomFieldValues
            .Where(v => v.EntityId == entityId && v.Field!.EntityType == entityType)
            .ToDictionaryAsync(v => v.FieldId, v => v.Value);
        return fields.Select(f => new CustomFieldInput(f, values.GetValueOrDefault(f.Id))).ToList();
    }

    /// <summary>Valeurs postées, telles que saisies (pour réafficher le formulaire en cas d'erreur).</summary>
    public static List<CustomFieldInput> FromPosted(List<CustomField> fields, Dictionary<int, string?> posted) =>
        fields.Select(f => new CustomFieldInput(f, posted.GetValueOrDefault(f.Id))).ToList();

    /// <summary>Valide et normalise les valeurs postées ; les erreurs vont dans <paramref name="modelState"/>.</summary>
    public static Dictionary<int, string?> Validate(List<CustomField> fields, Dictionary<int, string?> posted, ModelStateDictionary modelState)
    {
        Dictionary<int, string?> normalized = [];
        foreach (CustomField field in fields)
        {
            string key = $"{Prefix}[{field.Id}]";
            string? raw = posted.GetValueOrDefault(field.Id)?.Trim();
            if (string.IsNullOrEmpty(raw))
            {
                if (field.Type == CustomFieldType.Boolean)
                {
                    normalized[field.Id] = "false";
                }
                else if (field.Required)
                {
                    modelState.AddModelError(key, $"« {field.Name} » est obligatoire.");
                }
                else
                {
                    normalized[field.Id] = null;
                }
                continue;
            }
            if (TryNormalize(field, raw, out string? value))
            {
                normalized[field.Id] = value;
            }
            else
            {
                modelState.AddModelError(key, field.Type switch
                {
                    CustomFieldType.Number => $"« {field.Name} » doit être un nombre.",
                    CustomFieldType.Date => $"« {field.Name} » doit être une date.",
                    CustomFieldType.List => $"« {field.Name} » : choix inconnu.",
                    _ => $"« {field.Name} » est trop long (2000 caractères maximum).",
                });
            }
        }
        return normalized;
    }

    /// <summary>Normalise une valeur saisie selon le type du champ (forme invariante en base).</summary>
    public static bool TryNormalize(CustomField field, string raw, out string? value)
    {
        value = null;
        switch (field.Type)
        {
            case CustomFieldType.Number:
                if (!decimal.TryParse(raw.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number))
                {
                    return false;
                }
                value = number.ToString(CultureInfo.InvariantCulture);
                return true;
            case CustomFieldType.Date:
                if (!DateOnly.TryParse(raw, CultureInfo.InvariantCulture, out DateOnly date)
                    && !DateOnly.TryParse(raw, CultureInfo.GetCultureInfo("fr-FR"), out date))
                {
                    return false;
                }
                value = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                return true;
            case CustomFieldType.Boolean:
                value = raw is "true" or "on" or "1" or "oui" or "Oui" ? "true" : "false";
                return true;
            case CustomFieldType.List:
                value = field.OptionList.FirstOrDefault(o => string.Equals(o, raw, StringComparison.CurrentCultureIgnoreCase));
                return value is not null;
            default:
                value = raw;
                return raw.Length <= 2000;
        }
    }

    /// <summary>Enregistre les valeurs d'un objet : une valeur vide supprime l'enregistrement.</summary>
    public static async Task SaveAsync(AppDbContext db, int entityId, Dictionary<int, string?> values)
    {
        List<int> fieldIds = values.Keys.ToList();
        Dictionary<int, CustomFieldValue> existing = await db.CustomFieldValues
            .Where(v => v.EntityId == entityId && fieldIds.Contains(v.FieldId))
            .ToDictionaryAsync(v => v.FieldId);
        foreach (KeyValuePair<int, string?> pair in values)
        {
            CustomFieldValue? current = existing.GetValueOrDefault(pair.Key);
            if (pair.Value is null)
            {
                if (current is not null)
                {
                    db.CustomFieldValues.Remove(current);
                }
            }
            else if (current is null)
            {
                db.CustomFieldValues.Add(new CustomFieldValue { FieldId = pair.Key, EntityId = entityId, Value = pair.Value });
            }
            else
            {
                current.Value = pair.Value;
            }
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Valeurs de plusieurs objets d'un même type (colonnes des listes) : EntityId → (FieldId → valeur).</summary>
    public static async Task<Dictionary<int, Dictionary<int, string>>> ValuesForAsync(AppDbContext db, string entityType)
    {
        List<CustomFieldValue> values = await db.CustomFieldValues.Where(v => v.Field!.EntityType == entityType).ToListAsync();
        return values.GroupBy(v => v.EntityId).ToDictionary(g => g.Key, g => g.ToDictionary(v => v.FieldId, v => v.Value));
    }

    /// <summary>Valeur affichable (Oui/Non, date au format français).</summary>
    public static string Display(CustomField field, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }
        return field.Type switch
        {
            CustomFieldType.Boolean => value == "true" ? "Oui" : "Non",
            CustomFieldType.Date when DateOnly.TryParse(value, CultureInfo.InvariantCulture, out DateOnly date) => date.ToString("d", CultureInfo.CurrentCulture),
            CustomFieldType.Number when decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number) => number.ToString(CultureInfo.CurrentCulture),
            _ => value,
        };
    }
}
