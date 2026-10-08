using System.Security.Cryptography;
using System.Text;
using IPAMdotNet.Data;
using Microsoft.AspNetCore.Identity;

namespace IPAMdotNet.Maintenance;

/// <summary>
/// Vérification des mots de passe : hachés ASP.NET Core Identity, et hachés « $6$ » (SHA-512 crypt) des comptes importés
/// de phpIPAM. Ces derniers sont vérifiés puis signalés à re-hacher : ils disparaissent à la première connexion.
/// </summary>
public static class Passwords
{
    private static readonly PasswordHasher<User> Hasher = new();

    public static PasswordVerificationResult Verify(User user, string hash, string password)
    {
        if (hash.StartsWith("$6$", StringComparison.Ordinal))
        {
            return Sha512Crypt(password, hash) is { } computed
                && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(computed), Encoding.ASCII.GetBytes(hash))
                ? PasswordVerificationResult.SuccessRehashNeeded
                : PasswordVerificationResult.Failed;
        }
        return Hasher.VerifyHashedPassword(user, hash, password);
    }

    public static string Hash(User user, string password) => Hasher.HashPassword(user, password);

    private const string Alphabet = "./0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    /// <summary>
    /// crypt(3) SHA-512 (« $6$[rounds=N$]sel$… », spécification d'Ulrich Drepper), utilisé par phpIPAM.
    /// Renvoie le haché complet calculé avec le sel et le nombre de tours de <paramref name="setting"/> ; null si mal formé.
    /// </summary>
    public static string? Sha512Crypt(string password, string setting)
    {
        string rest = setting[3..];
        int rounds = 5000;
        bool customRounds = false;
        if (rest.StartsWith("rounds=", StringComparison.Ordinal))
        {
            int end = rest.IndexOf('$');
            if (end < 0 || !int.TryParse(rest[7..end], out rounds))
            {
                return null;
            }
            rounds = Math.Clamp(rounds, 1000, 999_999_999);
            customRounds = true;
            rest = rest[(end + 1)..];
        }
        int saltEnd = rest.IndexOf('$');
        string saltText = saltEnd < 0 ? rest : rest[..saltEnd];
        byte[] salt = Encoding.ASCII.GetBytes(saltText.Length > 16 ? saltText[..16] : saltText);
        byte[] key = Encoding.UTF8.GetBytes(password);

        byte[] b = SHA512.HashData([.. key, .. salt, .. key]);
        List<byte> a = [.. key, .. salt];
        for (int count = key.Length; count > 0; count -= 64)
        {
            a.AddRange(b.Take(Math.Min(count, 64)));
        }
        for (int count = key.Length; count > 0; count >>= 1)
        {
            a.AddRange((count & 1) != 0 ? b : key);
        }
        byte[] digestA = SHA512.HashData(a.ToArray());

        byte[] dp = SHA512.HashData(Enumerable.Repeat(key, key.Length).SelectMany(k => k).ToArray());
        byte[] p = Repeat(dp, key.Length);
        byte[] ds = SHA512.HashData(Enumerable.Repeat(salt, 16 + digestA[0]).SelectMany(s => s).ToArray());
        byte[] s = Repeat(ds, salt.Length);

        byte[] c = digestA;
        for (int i = 0; i < rounds; i++)
        {
            List<byte> round = [];
            round.AddRange((i & 1) != 0 ? p : c);
            if (i % 3 != 0)
            {
                round.AddRange(s);
            }
            if (i % 7 != 0)
            {
                round.AddRange(p);
            }
            round.AddRange((i & 1) != 0 ? c : p);
            c = SHA512.HashData(round.ToArray());
        }

        StringBuilder output = new("$6$");
        if (customRounds)
        {
            output.Append("rounds=").Append(rounds).Append('$');
        }
        output.Append(Encoding.ASCII.GetString(salt)).Append('$');
        int[][] groups =
        [
            [0, 21, 42], [22, 43, 1], [44, 2, 23], [3, 24, 45], [25, 46, 4], [47, 5, 26], [6, 27, 48], [28, 49, 7], [50, 8, 29], [9, 30, 51],
            [31, 52, 10], [53, 11, 32], [12, 33, 54], [34, 55, 13], [56, 14, 35], [15, 36, 57], [37, 58, 16], [59, 17, 38], [18, 39, 60],
            [40, 61, 19], [62, 20, 41],
        ];
        foreach (int[] g in groups)
        {
            Encode(output, (c[g[0]] << 16) | (c[g[1]] << 8) | c[g[2]], 4);
        }
        Encode(output, c[63], 2);
        return output.ToString();
    }

    private static byte[] Repeat(byte[] digest, int length) => Enumerable.Range(0, length).Select(i => digest[i % digest.Length]).ToArray();

    private static void Encode(StringBuilder output, int value, int count)
    {
        for (int i = 0; i < count; i++)
        {
            output.Append(Alphabet[value & 0x3f]);
            value >>= 6;
        }
    }
}
