using System.DirectoryServices.Protocols;
using System.Net;
using System.Text;
using IPAMdotNet.Data;

namespace IPAMdotNet.Maintenance;

public enum LdapResult
{
    Success,
    InvalidCredentials,
    ServerError,
}

/// <summary>Entrée de l'annuaire lue après la liaison : nom affiché, e-mail, noms (CN) des groupes.</summary>
public sealed record LdapUserInfo(string? DisplayName, string? Email, IReadOnlyList<string> Groups);

/// <summary>Authentification par liaison (bind) LDAP / Active Directory, puis lecture facultative de l'entrée du compte.</summary>
public static class LdapAuthenticator
{
    /// <param name="error">Détail technique en cas d'erreur serveur (journalisé, jamais affiché à l'utilisateur).</param>
    /// <param name="info">Entrée du compte si une base de recherche est configurée et que le compte y est trouvé ; null sinon.</param>
    public static LdapResult Verify(AuthMethod method, string userName, string password, out string? error, out LdapUserInfo? info)
    {
        error = null;
        info = null;
        // Un mot de passe vide ferait une liaison anonyme, acceptée par la plupart des annuaires.
        if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(userName))
        {
            return LdapResult.InvalidCredentials;
        }
        try
        {
            using LdapConnection connection = new(new LdapDirectoryIdentifier(method.Host, method.Port))
            {
                AuthType = AuthType.Basic,
                Timeout = TimeSpan.FromSeconds(method.TimeoutSeconds),
            };
            connection.SessionOptions.ProtocolVersion = 3;
            connection.SessionOptions.SecureSocketLayer = method.UseSsl;
            // Pas de suivi des référrals : sur AD, une recherche à la racine du domaine resterait bloquée sur les partitions DNS.
            connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            connection.Bind(new NetworkCredential(method.BindTemplate.Replace("{0}", EscapeDn(userName)), password));
            if (!string.IsNullOrWhiteSpace(method.SearchBase))
            {
                info = Search(connection, method, userName);
            }
            return LdapResult.Success;
        }
        catch (LdapException exception) when (exception.ErrorCode == 49)
        {
            return LdapResult.InvalidCredentials;
        }
        catch (Exception exception) when (exception is LdapException or DirectoryOperationException or PlatformNotSupportedException or DllNotFoundException)
        {
            error = $"{method.Name} ({method.Host}:{method.Port}) : {exception.Message}";
            return LdapResult.ServerError;
        }
    }

    /// <summary>Recherche du compte avec les droits de l'utilisateur lui-même (pas de compte de service à configurer).</summary>
    private static LdapUserInfo? Search(LdapConnection connection, AuthMethod method, string userName)
    {
        string filter = (string.IsNullOrWhiteSpace(method.UserFilter) ? AuthMethod.DefaultUserFilter : method.UserFilter)
            .Replace("{0}", EscapeFilter(userName));
        SearchRequest request = new(method.SearchBase, filter, SearchScope.Subtree, "displayName", "cn", "mail", "memberOf") { SizeLimit = 1 };
        SearchResponse response = (SearchResponse)connection.SendRequest(request);
        if (response.Entries.Count == 0)
        {
            return null;
        }
        SearchResultEntry entry = response.Entries[0];
        string? First(string attribute) =>
            entry.Attributes[attribute]?.GetValues(typeof(string)).OfType<string>().FirstOrDefault(v => v.Length > 0);
        List<string> groups = (entry.Attributes["memberOf"]?.GetValues(typeof(string)).OfType<string>() ?? [])
            .Select(CommonName).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new LdapUserInfo(First("displayName") ?? First("cn"), First("mail"), groups);
    }

    /// <summary>Valeur du premier composant d'un DN (« CN=Opérateurs,OU=Groupes,… » → « Opérateurs »), échappements retirés.</summary>
    public static string? CommonName(string dn)
    {
        int equals = dn.IndexOf('=');
        if (equals < 0)
        {
            return null;
        }
        StringBuilder value = new();
        for (int i = equals + 1; i < dn.Length; i++)
        {
            if (dn[i] == '\\' && i + 1 < dn.Length)
            {
                value.Append(dn[++i]);
            }
            else if (dn[i] is ',' or '+')
            {
                break;
            }
            else
            {
                value.Append(dn[i]);
            }
        }
        string text = value.ToString().Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>Échappement RFC 4514 : empêche d'injecter des composants de DN via le nom d'utilisateur.</summary>
    private static string EscapeDn(string value)
    {
        StringBuilder builder = new();
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            bool special = c is ',' or '+' or '"' or '\\' or '<' or '>' or ';' or '='
                || (i == 0 && c is ' ' or '#') || (i == value.Length - 1 && c == ' ');
            builder.Append(special ? "\\" + c : c.ToString());
        }
        return builder.ToString();
    }

    /// <summary>Échappement RFC 4515 : empêche d'élargir le filtre de recherche (« * », parenthèses…).</summary>
    private static string EscapeFilter(string value)
    {
        StringBuilder builder = new();
        foreach (char c in value)
        {
            builder.Append(c switch
            {
                '*' => "\\2a",
                '(' => "\\28",
                ')' => "\\29",
                '\\' => "\\5c",
                '\0' => "\\00",
                _ => c.ToString(),
            });
        }
        return builder.ToString();
    }
}
