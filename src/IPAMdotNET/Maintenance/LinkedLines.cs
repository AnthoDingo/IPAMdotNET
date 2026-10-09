using IPAMdotNet.Localization;
using IPAMdotNet.Data;
using IPAMdotNet.Networking;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace IPAMdotNet.Maintenance;

/// <summary>
/// Zone de saisie « une adresse ou un réseau par ligne », chaque ligne liée à l'objet correspondant de l'IPAM
/// (côtés d'une règle NAT, sous-réseaux d'un pair BGP).
/// </summary>
public sealed class LinkedLinesForm
{
    public string? Text { get; set; }

    /// <summary>Par ligne : « a:id », « s:id », « none » (texte libre) ou vide (liaison automatique).</summary>
    public List<string?> Choices { get; set; } = [];

    /// <summary>Lignes ambiguës : objets proposés.</summary>
    public Dictionary<int, List<NatCandidate>> Candidates { get; } = [];

    /// <summary>Lignes liées : libellé de l'objet.</summary>
    public Dictionary<int, string> Linked { get; } = [];
}

public static class LinkedLines
{
    /// <summary>
    /// Résout chaque ligne avec le choix de même rang ; un lien ne vaut plus si le texte de la ligne a changé (nouvelle liaison
    /// automatique). Les erreurs vont dans <paramref name="state"/> (champ <paramref name="field"/>), les objets proposés dans le
    /// formulaire, qui est réécrit dans la forme canonique. <paramref name="subnetsOnly"/> : réseaux de l'IPAM uniquement (pas
    /// d'adresse, pas de texte libre).
    /// </summary>
    public static async Task<List<NatSide>> ResolveAsync(AppDbContext db, LinkedLinesForm form, ModelStateDictionary state, string field, bool subnetsOnly)
    {
        // Valeur postée oubliée : la zone est réaffichée dans la forme canonique (les erreurs ajoutées ensuite restent).
        state.Remove(field);
        List<string> lines = NatLinks.Lines(form.Text);
        List<NatSide> sides = [];
        List<string?> choices = [];
        for (int i = 0; i < lines.Count; i++)
        {
            string? choice = form.Choices.ElementAtOrDefault(i);
            NatSide side = await NatLinks.ResolveAsync(db, lines[i], subnetsOnly && choice == NatLinks.None ? null : choice);
            if (choice is not null && choice != NatLinks.None && side.Error is null && Ip.TryNormalize(lines[i], out string typed) && typed != side.Text)
            {
                side = await NatLinks.ResolveAsync(db, lines[i], null);
            }
            if (subnetsOnly && side.Error is null && side.SubnetId is null)
            {
                side = side with { Error = side.Text.Contains('/') ? L.T("Aucun sous-réseau de l'IPAM ne correspond.") : L.T("Réseau attendu (ex. 10.0.0.0/24).") };
            }
            if (side.Error is not null)
            {
                state.AddModelError(field, $"« {lines[i]} » : {side.Error}");
                if (side.Candidates is not null)
                {
                    form.Candidates[i] = side.Candidates;
                }
            }
            choices.Add(NatLinks.Key(side.SubnetId, side.AddressId) ?? (choice == NatLinks.None ? NatLinks.None : null));
            sides.Add(side);
        }
        form.Text = string.Join('\n', sides.Select(s => s.Text));
        form.Choices = choices;
        return sides;
    }

    /// <summary>Formulaire d'objets enregistrés (texte actualisé depuis les objets liés).</summary>
    public static async Task<LinkedLinesForm> FormAsync(AppDbContext db, IEnumerable<(string Text, int? SubnetId, int? AddressId)> items)
    {
        LinkedLinesForm form = new();
        List<string> lines = [];
        foreach ((string text, int? subnetId, int? addressId) in items)
        {
            string? key = NatLinks.Key(subnetId, addressId);
            lines.Add((await NatLinks.ResolveAsync(db, text, key ?? NatLinks.None)).Text);
            form.Choices.Add(key);
        }
        form.Text = string.Join('\n', lines);
        return form;
    }

    /// <summary>Libellés des objets liés, pour l'affichage.</summary>
    public static async Task LabelAsync(AppDbContext db, LinkedLinesForm form)
    {
        List<string> lines = NatLinks.Lines(form.Text);
        for (int i = 0; i < lines.Count; i++)
        {
            if (form.Choices.ElementAtOrDefault(i) is { } key && key != NatLinks.None && Ip.TryNormalize(lines[i], out string normalized)
                && (await NatLinks.CandidatesAsync(db, normalized)).FirstOrDefault(c => c.Key == key) is { } candidate)
            {
                form.Linked[i] = candidate.Label;
            }
        }
    }
}
