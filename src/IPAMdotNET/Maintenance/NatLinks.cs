using System.Net;
using IPAMdotNet.Data;
using IPAMdotNet.Networking;
using Microsoft.EntityFrameworkCore;

namespace IPAMdotNet.Maintenance;

/// <summary>Objet proposé pour un côté d'une règle NAT : clé « a:12 » (adresse) ou « s:3 » (sous-réseau).</summary>
public sealed record NatCandidate(string Key, string Label);

/// <summary>Côté résolu d'une règle NAT : texte canonique et objet lié (au plus un), ou erreur / choix à faire.</summary>
public sealed record NatSide(string Text, int? SubnetId, int? AddressId, string? Error = null, List<NatCandidate>? Candidates = null);

/// <summary>Liaison des règles NAT aux sous-réseaux et adresses (formulaire et API).</summary>
public static class NatLinks
{
    public const string None = "none";

    /// <summary>
    /// <paramref name="choice"/> : « a:id » / « s:id » = objet imposé, <see cref="None"/> = texte libre, null = automatique :
    /// le texte est lié à l'objet qui lui correspond exactement s'il est seul, sinon les candidats sont renvoyés pour un choix.
    /// </summary>
    public static async Task<NatSide> ResolveAsync(AppDbContext db, string? text, string? choice)
    {
        if (choice is not null && choice != None && TryParseKey(choice, out char kind, out int id))
        {
            if (kind == 'a')
            {
                byte[]? address = await db.IpAddresses.Where(a => a.Id == id).Select(a => a.Address).SingleOrDefaultAsync();
                return address is null ? new NatSide(text ?? "", null, null, "Adresse inexistante.") : new NatSide(Ip.FromBytes(address).ToString(), null, id);
            }
            Subnet? subnet = await db.Subnets.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id);
            return subnet is null ? new NatSide(text ?? "", null, null, "Sous-réseau inexistant.") : new NatSide(subnet.Network.ToString(), id, null);
        }
        if (!Ip.TryNormalize(text, out string normalized))
        {
            return new NatSide(text ?? "", null, null, "Adresse ou réseau invalide (ex. 10.0.0.1 ou 10.0.0.0/24).");
        }
        if (choice == None)
        {
            return new NatSide(normalized, null, null);
        }
        List<NatCandidate> candidates = await CandidatesAsync(db, normalized);
        return candidates.Count switch
        {
            0 => new NatSide(normalized, null, null),
            1 => await ResolveAsync(db, normalized, candidates[0].Key),
            _ => new NatSide(normalized, null, null, "Plusieurs objets correspondent : choisissez celui à lier.", candidates),
        };
    }

    /// <summary>Clé de l'objet lié à un côté (pour réafficher le formulaire), null sans lien.</summary>
    public static string? Key(int? subnetId, int? addressId) =>
        addressId is int a ? $"a:{a}" : subnetId is int s ? $"s:{s}" : null;

    /// <summary>Objets correspondant exactement au texte : adresses pour une adresse, sous-réseaux pour un réseau.</summary>
    public static async Task<List<NatCandidate>> CandidatesAsync(AppDbContext db, string normalized)
    {
        if (normalized.Contains('/'))
        {
            IPNetwork network = IPNetwork.Parse(normalized);
            byte[] bytes = Ip.ToBytes(network.BaseAddress);
            return (await db.Subnets.Where(s => s.Address == bytes && s.PrefixLength == network.PrefixLength)
                    .Select(s => new { s.Id, Section = s.Section!.Name, s.Description }).ToListAsync())
                .Select(s => new NatCandidate($"s:{s.Id}", $"{normalized} — {s.Section}{(s.Description is null ? "" : $" ({s.Description})")}")).ToList();
        }
        byte[] address = Ip.ToBytes(IPAddress.Parse(normalized));
        return (await db.IpAddresses.Where(a => a.Address == address)
                .Select(a => new { a.Id, Section = a.Subnet!.Section!.Name, SubnetAddress = a.Subnet.Address, a.Subnet.PrefixLength, a.Hostname }).ToListAsync())
            .Select(a => new NatCandidate($"a:{a.Id}",
                $"{normalized} — {a.Section}, {new IPNetwork(Ip.FromBytes(a.SubnetAddress), a.PrefixLength)}{(a.Hostname is null ? "" : $" ({a.Hostname})")}")).ToList();
    }

    private static bool TryParseKey(string key, out char kind, out int id)
    {
        kind = key.Length > 2 ? key[0] : '\0';
        id = 0;
        return kind is 'a' or 's' && key[1] == ':' && int.TryParse(key[2..], out id);
    }
}
