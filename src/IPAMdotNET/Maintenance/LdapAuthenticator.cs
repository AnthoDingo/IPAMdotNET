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

/// <summary>Authentification par liaison (bind) LDAP / Active Directory.</summary>
public static class LdapAuthenticator
{
    /// <param name="error">Détail technique en cas d'erreur serveur (journalisé, jamais affiché à l'utilisateur).</param>
    public static LdapResult Verify(AuthMethod method, string userName, string password, out string? error)
    {
        error = null;
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
            connection.Bind(new NetworkCredential(method.BindTemplate.Replace("{0}", EscapeDn(userName)), password));
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
}
