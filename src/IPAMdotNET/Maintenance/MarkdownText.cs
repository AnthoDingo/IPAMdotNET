using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace IPAMdotNet.Maintenance;

/// <summary>
/// Rendu Markdown sûr (instructions) : HTML brut désactivé (échappé), liens et images limités à http(s), mailto et chemins relatifs.
/// </summary>
public static class MarkdownText
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAutoLinks()
        .UseTaskLists()
        .UseEmphasisExtras()
        // Les instructions rédigées en texte brut gardent leurs retours à la ligne.
        .UseSoftlineBreakAsHardlineBreak()
        .DisableHtml()
        .Build();

    public static string ToHtml(string? markdown)
    {
        MarkdownDocument document = Markdown.Parse(markdown ?? "", Pipeline);
        foreach (LinkInline link in document.Descendants<LinkInline>())
        {
            if (!IsSafe(link.Url))
            {
                link.Url = "#";
            }
        }
        foreach (AutolinkInline link in document.Descendants<AutolinkInline>())
        {
            if (!IsSafe(link.Url))
            {
                link.Url = "#";
            }
        }
        return document.ToHtml(Pipeline);
    }

    /// <summary>Pas de javascript:, data:, vbscript:… : seulement http(s), mailto, ancres et chemins relatifs.</summary>
    public static bool IsSafe(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return true;
        }
        string trimmed = url.Trim();
        if (trimmed.StartsWith('/') || trimmed.StartsWith('#') || trimmed.StartsWith('?'))
        {
            return !trimmed.StartsWith("//", StringComparison.Ordinal);
        }
        return Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri)
            ? uri.Scheme is "http" or "https" or "mailto"
            : !trimmed.Contains(':');
    }
}
