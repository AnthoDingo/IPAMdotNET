using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace IPAMdotNet.Networking;

/// <summary>
/// Sondage d'hôtes : ping, puis ports TCP si configurés ; résolution DNS inverse.
/// Sans dépendance au reste de l'application : ce fichier est aussi compilé dans l'agent distant (IPAMdotNet.ScanAgent).
/// </summary>
public static class HostProbe
{
    /// <summary>Adresses qui répondent au ping ou, à défaut, sur l'un des ports TCP.</summary>
    public static async Task<HashSet<IPAddress>> ProbeAllAsync(IEnumerable<IPAddress> addresses, TimeSpan timeout, int parallelism, int[] tcpPorts,
        CancellationToken cancellationToken)
    {
        using SemaphoreSlim slots = new(parallelism);
        IEnumerable<Task<IPAddress?>> probes = addresses.Select(async address =>
        {
            await slots.WaitAsync(cancellationToken);
            try
            {
                if (await PingAsync(address, timeout, cancellationToken))
                {
                    return address;
                }
                // Ports essayés en parallèle : un hôte éteint coûte un seul délai, pas un par port.
                bool[] open = await Task.WhenAll(tcpPorts.Select(port => TcpAsync(address, port, timeout, cancellationToken)));
                return open.Any(o => o) ? address : null;
            }
            finally
            {
                slots.Release();
            }
        });
        IPAddress?[] results = await Task.WhenAll(probes);
        return results.OfType<IPAddress>().ToHashSet();
    }

    public static async Task<bool> PingAsync(IPAddress address, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            // Une instance de Ping par envoi : la classe n'accepte pas d'envois simultanés.
            using Ping ping = new();
            PingReply reply = await ping.SendPingAsync(address, timeout, cancellationToken: cancellationToken);
            return reply.Status == IPStatus.Success;
        }
        catch (PingException)
        {
            return false;
        }
    }

    /// <summary>
    /// Connexion TCP : acceptée ou refusée (RST), l'hôte est présent ; sans réponse dans le délai, il est considéré absent.
    /// Sous Windows, un refus n'arrive qu'après ~2 s (le SYN est réémis).
    /// </summary>
    public static async Task<bool> TcpAsync(IPAddress address, int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        using Socket socket = new(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(address, port, limit.Token);
            return true;
        }
        catch (SocketException e)
        {
            return e.SocketErrorCode == SocketError.ConnectionRefused;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>Recherches de noms simultanées au plus (voir <see cref="ResolveAsync"/>).</summary>
    private const int ResolveParallelism = 32;

    /// <summary>
    /// Nom DNS inverse (100 caractères au plus) ; null si aucun nom. Résolveur du système (DNS, fichier hosts, NetBIOS / LLMNR
    /// sous Windows) : appel bloquant, jusqu'à une dizaine de secondes pour une adresse sans nom. Il s'exécute donc sur un thread
    /// dédié et le délai court à partir de son début : lancées toutes ensemble sur le pool de threads, les recherches lentes
    /// l'affamaient et le délai écartait aussi les adresses qui avaient un nom.
    /// </summary>
    public static async Task<string?> ResolveAsync(IPAddress address)
    {
        try
        {
            Task<IPHostEntry> lookup = Task.Factory.StartNew(() => Dns.GetHostEntry(address), CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
            if (await Task.WhenAny(lookup, Task.Delay(TimeSpan.FromSeconds(15))) != lookup)
            {
                return null;
            }
            string name = (await lookup).HostName;
            return name == address.ToString() || string.IsNullOrEmpty(name) ? null : name.Length > 100 ? name[..100] : name;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    /// <summary>Noms DNS inverses résolus en parallèle ; seules les adresses qui ont un nom figurent dans le résultat.</summary>
    public static async Task<Dictionary<IPAddress, string>> ResolveAllAsync(IEnumerable<IPAddress> addresses)
    {
        using SemaphoreSlim slots = new(ResolveParallelism);
        (IPAddress Address, string? Name)[] names = await Task.WhenAll(addresses.Select(async a =>
        {
            await slots.WaitAsync();
            try
            {
                return (a, await ResolveAsync(a));
            }
            finally
            {
                slots.Release();
            }
        }));
        return names.Where(n => n.Name is not null).ToDictionary(n => n.Address, n => n.Name!);
    }

    /// <summary>
    /// Table ARP du système (IPv4 → MAC aa:bb:cc:dd:ee:ff) : /proc/net/arp sous Linux, « arp -a » ailleurs. À lire juste après le ping,
    /// qui la remplit ; ne contient que les hôtes du même segment L2 (pas de MAC au-delà d'un routeur). Vide en cas d'échec.
    /// </summary>
    public static async Task<Dictionary<IPAddress, string>> ReadArpTableAsync(CancellationToken cancellationToken)
    {
        string text;
        try
        {
            if (File.Exists("/proc/net/arp"))
            {
                text = await File.ReadAllTextAsync("/proc/net/arp", cancellationToken);
            }
            else
            {
                using Process process = Process.Start(new ProcessStartInfo("arp", OperatingSystem.IsWindows() ? "-a" : "-an")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }) ?? throw new InvalidOperationException("arp introuvable.");
                text = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return [];
        }
        return ParseArpTable(text);
    }

    /// <summary>Lignes portant une adresse IPv4 et une MAC (formats Linux, Windows, BSD/macOS) ; MAC nulle ou de diffusion ignorée.</summary>
    public static Dictionary<IPAddress, string> ParseArpTable(string text)
    {
        Dictionary<IPAddress, string> table = [];
        foreach (string line in text.Split('\n'))
        {
            string[] tokens = line.Split([' ', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries);
            IPAddress? address = tokens
                .Select(t => IPAddress.TryParse(t.Trim('(', ')'), out IPAddress? ip) && ip.AddressFamily == AddressFamily.InterNetwork ? ip : null)
                .FirstOrDefault(ip => ip is not null);
            string? mac = tokens.Select(ToMac).FirstOrDefault(m => m is not null);
            if (address is not null && mac is not null and not "00:00:00:00:00:00" and not "ff:ff:ff:ff:ff:ff")
            {
                table.TryAdd(address, mac);
            }
        }
        return table;
    }

    // 6 octets hexadécimaux séparés par « : » ou « - » ; macOS omet le zéro de tête (0:1b:…).
    private static string? ToMac(string token)
    {
        string[] parts = token.Split(':', '-');
        return parts.Length == 6 && parts.All(p => p.Length is 1 or 2 && p.All(Uri.IsHexDigit))
            ? string.Join(':', parts.Select(p => p.PadLeft(2, '0'))).ToLowerInvariant()
            : null;
    }
}
