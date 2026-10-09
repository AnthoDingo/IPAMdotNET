using IPAMdotNet.Localization;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using IPAMdotNet.Networking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace IPAMdotNet.Pages.Tools.Calculator;

public sealed record CalculatorResult(IPAddress Address, IPNetwork Network, IReadOnlyList<(string Label, string Value)> Rows);

public class IndexModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public CalculatorResult? Result { get; private set; }
    public string? Error { get; private set; }

    public void OnGet()
    {
        if (string.IsNullOrWhiteSpace(Q))
        {
            return;
        }
        Result = Calculate(Q.Trim());
        Error = Result is null ? L.T("Saisie invalide. Exemples : 192.168.1.10/24, 10.0.0.0/8, 2001:db8::1/64, 172.16.5.4") : null;
    }

    /// <summary>Accepte « adresse/préfixe » (bits d'hôte autorisés : c'est un calculateur) ou une adresse seule (/32, /128).</summary>
    private static CalculatorResult? Calculate(string input)
    {
        string addressText = input.Contains('/') ? input[..input.IndexOf('/')] : input;
        if (!IPAddress.TryParse(addressText, out IPAddress? address))
        {
            return null;
        }
        bool ipv4 = address.AddressFamily == AddressFamily.InterNetwork;
        string cidr = input.Contains('/') ? input : $"{input}/{(ipv4 ? 32 : 128)}";
        if (!IPNetwork.TryParse(cidr, out IPNetwork network))
        {
            return null;
        }

        int bits = ipv4 ? 32 : 128;
        int prefix = network.PrefixLength;
        BigInteger count = Ip.AddressCount(network);
        IPAddress last = Ip.LastAddress(network);
        List<(string, string)> rows =
        [
            (L.T("Adresse"), address.ToString()),
            (L.T("Réseau"), network.ToString()),
            (L.T("Type"), L.T(Ip.Classify(network))),
        ];

        if (ipv4)
        {
            IPAddress mask = Ip.Ipv4Mask(prefix);
            IPAddress wildcard = new(mask.GetAddressBytes().Select(b => (byte)~b).ToArray());
            // /31 (RFC 3021) et /32 : pas d'adresse réseau ni de diffusion réservées.
            bool pointToPoint = prefix >= 31;
            IPAddress firstHost = pointToPoint ? network.BaseAddress : Offset(network.BaseAddress, 1);
            IPAddress lastHost = pointToPoint ? last : Offset(last, -1);
            BigInteger hosts = pointToPoint ? count : count - 2;
            rows.Add((L.T("Masque"), $"{mask} (/{prefix})"));
            rows.Add((L.T("Masque inverse"), wildcard.ToString()));
            rows.Add((L.T("Adresse de diffusion"), pointToPoint ? "—" : last.ToString()));
            rows.Add((L.T("Première adresse utilisable"), firstHost.ToString()));
            rows.Add((L.T("Dernière adresse utilisable"), lastHost.ToString()));
            rows.Add((L.T("Adresses utilisables"), hosts.ToString("N0", CultureInfo.CurrentCulture)));
        }
        else
        {
            rows.Add((L.T("Préfixe"), $"/{prefix}"));
            rows.Add((L.T("Première adresse"), network.BaseAddress.ToString()));
            rows.Add((L.T("Dernière adresse"), last.ToString()));
            rows.Add((L.T("Adresse développée"), Expand(address)));
        }
        rows.Add((L.T("Nombre d'adresses"), count.ToString("N0", CultureInfo.CurrentCulture)));
        rows.Add((L.T("Bits d'hôte"), (bits - prefix).ToString(CultureInfo.InvariantCulture)));
        rows.Add((L.T("Zone DNS inverse"), Ip.ReverseZone(network)));
        return new CalculatorResult(address, network, rows);
    }

    private static IPAddress Offset(IPAddress address, int delta)
    {
        byte[] bytes = address.GetAddressBytes();
        uint value = (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
        value = (uint)(value + delta);
        return new IPAddress([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
    }

    private static string Expand(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        return string.Join(':', Enumerable.Range(0, 8).Select(i => (bytes[i * 2] << 8 | bytes[i * 2 + 1]).ToString("x4", CultureInfo.InvariantCulture)));
    }
}
