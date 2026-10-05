using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Localization;

namespace IPAMdotNet.Localization;

/// <summary>
/// Traductions de l'interface, à la manière de gettext (comme phpIPAM) : la clé est le texte français d'origine, traduit via
/// <c>Localization/i18n/&lt;langue&gt;.json</c> (ressources incorporées, { "texte français": "traduction" }).
/// Repli : langue exacte, puis anglais (en-GB) pour toute langue autre que le français, puis le texte français.
/// </summary>
public static class L
{
    /// <summary>Langue source des textes (aucun fichier de traduction).</summary>
    public const string Source = "fr-FR";

    /// <summary>Langue de repli des autres langues : fichier de référence pour les traducteurs.</summary>
    public const string Fallback = "en-GB";

    /// <summary>Langues de phpIPAM, avec leur nom dans la langue elle-même.</summary>
    public static readonly IReadOnlyList<(string Code, string Name)> Languages =
    [
        ("fr-FR", "Français"),
        ("en-GB", "English (UK)"),
        ("en-US", "English (US)"),
        ("cs-CZ", "Čeština"),
        ("de-DE", "Deutsch"),
        ("es-ES", "Español"),
        ("it-IT", "Italiano"),
        ("ja-JP", "日本語"),
        ("nl-NL", "Nederlands"),
        ("pt-BR", "Português (Brasil)"),
        ("ru-RU", "Русский"),
        ("sl-SI", "Slovenščina"),
        ("zh-CN", "简体中文"),
        ("zh-TW", "繁體中文"),
    ];

    public static bool IsSupported(string? code) => Languages.Any(l => l.Code == code);

    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Catalogs = new();

    /// <summary>Texte traduit dans la langue de l'interface courante.</summary>
    public static string T(string text) => Translate(text, CultureInfo.CurrentUICulture.Name);

    /// <summary>Texte traduit puis mis en forme (<c>{0}</c>, <c>{1}</c>… dans la clé française).</summary>
    public static string T(string text, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(text), args);

    /// <summary>Texte traduit échappé pour une chaîne JavaScript entre apostrophes (ex. <c>confirm('…')</c> dans un attribut).</summary>
    public static string Js(string text, params object?[] args) => JavaScriptEncoder.Default.Encode(args.Length == 0 ? T(text) : T(text, args));

    public static string Translate(string text, string culture)
    {
        if (culture == Source || !IsSupported(culture))
        {
            return text;
        }
        return Catalog(culture).GetValueOrDefault(text) ?? Catalog(Fallback).GetValueOrDefault(text) ?? text;
    }

    /// <summary>Catalogue d'une langue ; vide si le fichier manque.</summary>
    public static IReadOnlyDictionary<string, string> Catalog(string culture) => Catalogs.GetOrAdd(culture, code =>
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"i18n/{code}.json");
        if (stream is null)
        {
            return new Dictionary<string, string>();
        }
        Dictionary<string, string> entries = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
        // Une entrée vide n'est pas traduite (fichier en cours de traduction) : repli sur la langue suivante.
        return entries.Where(e => !string.IsNullOrEmpty(e.Value)).ToDictionary();
    });
}

/// <summary>
/// Branche <see cref="L"/> sur la localisation d'ASP.NET Core : libellés <c>[Display]</c>, messages de validation, noms des valeurs d'énumération.
/// Un seul catalogue pour tous les types : la clé est le texte français.
/// </summary>
public sealed class CatalogLocalizer : IStringLocalizer, IStringLocalizerFactory
{
    public LocalizedString this[string name] => Localize(name, L.T(name));

    public LocalizedString this[string name, params object[] arguments] => Localize(name, L.T(name, arguments));

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
        L.Catalog(CultureInfo.CurrentUICulture.Name).Select(e => new LocalizedString(e.Key, e.Value));

    public IStringLocalizer Create(Type resourceSource) => this;

    public IStringLocalizer Create(string baseName, string location) => this;

    private static LocalizedString Localize(string name, string value) => new(name, value, resourceNotFound: value == name);
}
