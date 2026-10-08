using System.Globalization;
using System.Net;
using System.Numerics;

namespace IPAMdotNet.Networking;

public static class Ip
{
    /// <summary>Format de stockage portable : 16 octets, IPv4 mappée en IPv6 (::ffff:a.b.c.d). Tri binaire identique sur les trois moteurs.</summary>
    public static byte[] ToBytes(IPAddress address) => address.MapToIPv6().GetAddressBytes();

    public static IPAddress FromBytes(byte[] bytes)
    {
        IPAddress address = new(bytes);
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    public static IPAddress LastAddress(IPNetwork network)
    {
        byte[] bytes = network.BaseAddress.GetAddressBytes();
        int hostBits = bytes.Length * 8 - network.PrefixLength;
        for (int i = bytes.Length - 1; hostBits > 0; i--, hostBits -= 8)
        {
            bytes[i] |= (byte)(hostBits >= 8 ? 0xFF : (1 << hostBits) - 1);
        }
        return new IPAddress(bytes);
    }

    /// <summary>Masque décimal pointé (IPv4 uniquement), ex. 24 → 255.255.255.0.</summary>
    public static IPAddress Ipv4Mask(int prefixLength)
    {
        uint mask = prefixLength == 0 ? 0 : uint.MaxValue << (32 - prefixLength);
        return new IPAddress([(byte)(mask >> 24), (byte)(mask >> 16), (byte)(mask >> 8), (byte)mask]);
    }

    private static readonly (IPNetwork Network, string Label)[] SpecialRanges =
    [
        (IPNetwork.Parse("0.0.0.0/8"), "Ce réseau (RFC 1122)"),
        (IPNetwork.Parse("10.0.0.0/8"), "Privée (RFC 1918)"),
        (IPNetwork.Parse("100.64.0.0/10"), "Partagée / CGNAT (RFC 6598)"),
        (IPNetwork.Parse("127.0.0.0/8"), "Boucle locale"),
        (IPNetwork.Parse("169.254.0.0/16"), "Lien local"),
        (IPNetwork.Parse("172.16.0.0/12"), "Privée (RFC 1918)"),
        (IPNetwork.Parse("192.0.2.0/24"), "Documentation (RFC 5737)"),
        (IPNetwork.Parse("192.168.0.0/16"), "Privée (RFC 1918)"),
        (IPNetwork.Parse("198.18.0.0/15"), "Tests de performance (RFC 2544)"),
        (IPNetwork.Parse("198.51.100.0/24"), "Documentation (RFC 5737)"),
        (IPNetwork.Parse("203.0.113.0/24"), "Documentation (RFC 5737)"),
        (IPNetwork.Parse("224.0.0.0/4"), "Multicast"),
        (IPNetwork.Parse("255.255.255.255/32"), "Diffusion limitée"),
        (IPNetwork.Parse("240.0.0.0/4"), "Réservée"),
        (IPNetwork.Parse("::/128"), "Non spécifiée"),
        (IPNetwork.Parse("::1/128"), "Boucle locale"),
        (IPNetwork.Parse("::ffff:0:0/96"), "IPv4 mappée"),
        (IPNetwork.Parse("2001:db8::/32"), "Documentation (RFC 3849)"),
        (IPNetwork.Parse("fc00::/7"), "Locale unique (ULA)"),
        (IPNetwork.Parse("fe80::/10"), "Lien local"),
        (IPNetwork.Parse("ff00::/8"), "Multicast"),
        (IPNetwork.Parse("2000::/3"), "Unicast global"),
    ];

    /// <summary>Plage spéciale (RFC) contenant le réseau, sinon « Publique ».</summary>
    public static string Classify(IPNetwork network) =>
        SpecialRanges.FirstOrDefault(r => Contains(r.Network, network)).Label ?? "Publique";

    /// <summary>Zone DNS inverse couvrant le réseau (octets entiers en IPv4, quartets en IPv6).</summary>
    public static string ReverseZone(IPNetwork network)
    {
        byte[] bytes = network.BaseAddress.GetAddressBytes();
        if (bytes.Length == 4)
        {
            IEnumerable<string> octets = bytes.Take(network.PrefixLength / 8).Reverse().Select(b => b.ToString(CultureInfo.InvariantCulture));
            return string.Join('.', octets.Append("in-addr.arpa"));
        }
        IEnumerable<string> nibbles = bytes.SelectMany(b => new[] { b >> 4, b & 0xF })
            .Take(network.PrefixLength / 4).Reverse().Select(n => n.ToString("x", CultureInfo.InvariantCulture));
        return string.Join('.', nibbles.Append("ip6.arpa"));
    }

    /// <summary>Valeur numérique d'une adresse (pour comparer, compter, avancer).</summary>
    public static BigInteger ToNumber(IPAddress address) =>
        new(address.GetAddressBytes(), isUnsigned: true, isBigEndian: true);

    public static IPAddress FromNumber(BigInteger value, bool ipv4)
    {
        byte[] raw = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        byte[] bytes = new byte[ipv4 ? 4 : 16];
        Array.Copy(raw, 0, bytes, bytes.Length - raw.Length, raw.Length);
        return new IPAddress(bytes);
    }

    /// <summary>
    /// Plage des adresses attribuables : en IPv4 (préfixe &lt; 31) sans l'adresse réseau ni la diffusion ;
    /// en IPv6, sans l'adresse anycast du routeur (première adresse), comme phpIPAM.
    /// </summary>
    public static (BigInteger First, BigInteger Last) UsableRange(IPNetwork network)
    {
        BigInteger first = ToNumber(network.BaseAddress);
        BigInteger last = ToNumber(LastAddress(network));
        bool ipv4 = network.BaseAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
        if (ipv4 && network.PrefixLength >= 31)
        {
            return (first, last);
        }
        return ipv4 ? (first + 1, last - 1) : (first + 1, last);
    }

    public static BigInteger UsableCount(IPNetwork network)
    {
        (BigInteger first, BigInteger last) = UsableRange(network);
        return last >= first ? last - first + 1 : 0;
    }

    /// <summary>Première adresse attribuable absente de <paramref name="used"/> (recherche bornée à 65 536 candidats).</summary>
    public static IPAddress? FirstFree(IPNetwork network, IReadOnlySet<BigInteger> used)
    {
        (BigInteger first, BigInteger last) = UsableRange(network);
        bool ipv4 = network.BaseAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;
        for (BigInteger candidate = first; candidate <= last && candidate - first < 65536; candidate++)
        {
            if (!used.Contains(candidate))
            {
                return FromNumber(candidate, ipv4);
            }
        }
        return null;
    }

    public static BigInteger AddressCount(IPNetwork network) =>
        BigInteger.One << (network.BaseAddress.GetAddressBytes().Length * 8 - network.PrefixLength);

    /// <summary>True si <paramref name="outer"/> contient <paramref name="inner"/> (ou lui est égal).</summary>
    public static bool Contains(IPNetwork outer, IPNetwork inner) =>
        outer.PrefixLength <= inner.PrefixLength && outer.Contains(inner.BaseAddress);

    /// <summary>
    /// Comme <see cref="IPNetwork.TryParse(string, out IPNetwork)"/>, mais refuse les bits d'hôte (10.0.0.1/24)
    /// au lieu de les masquer silencieusement.
    /// </summary>
    public static bool TryParseNetwork(string? text, out IPNetwork network)
    {
        string value = text?.Trim() ?? "";
        if (!IPNetwork.TryParse(value, out network))
        {
            return false;
        }
        return IPAddress.TryParse(value[..value.IndexOf('/')], out IPAddress? address) && address.Equals(network.BaseAddress);
    }

    /// <summary>Accepte une adresse (10.0.0.1) ou un réseau (10.0.0.0/24) et renvoie sa forme canonique.</summary>
    public static bool TryNormalize(string? text, out string normalized)
    {
        normalized = "";
        string value = text?.Trim() ?? "";
        if (value.Contains('/'))
        {
            if (!TryParseNetwork(value, out IPNetwork network))
            {
                return false;
            }
            normalized = network.ToString();
            return true;
        }
        if (!IPAddress.TryParse(value, out IPAddress? address))
        {
            return false;
        }
        normalized = address.ToString();
        return true;
    }
}
