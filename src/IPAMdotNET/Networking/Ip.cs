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
