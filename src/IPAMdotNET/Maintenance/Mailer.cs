using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using IPAMdotNet.Data;
using Microsoft.AspNetCore.DataProtection;

namespace IPAMdotNet.Maintenance;

/// <summary>
/// Envoi d'e-mails via le SMTP configuré (Administration › Messagerie). Un échec n'interrompt jamais l'action
/// qui l'a déclenché : il est inscrit au journal système.
/// </summary>
public static class Mailer
{
    public const string ProtectionPurpose = "IPAMdotNet.Mail.Password";

    /// <returns>null si l'envoi a réussi, sinon le message d'erreur.</returns>
    public static async Task<string?> SendAsync(AppDbContext db, IDataProtectionProvider protection, IEnumerable<string> recipients, string subject, string body)
    {
        List<string> to = recipients.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        MailSettings settings = await SettingsStore.LoadAsync<MailSettings>(db, SettingsStore.MailPrefix);
        if (!settings.IsUsable || to.Count == 0)
        {
            return settings.IsUsable ? "Aucun destinataire." : "La messagerie n'est pas configurée.";
        }
        try
        {
            using MailMessage message = new()
            {
                From = new MailAddress(settings.FromAddress!, settings.FromName ?? SettingsStore.Server.SiteTitle),
                Subject = $"[{SettingsStore.Server.SiteTitle}] {subject}",
                Body = body,
            };
            foreach (string recipient in to)
            {
                message.To.Add(recipient);
            }
            using SmtpClient client = new(settings.Host, settings.Port) { EnableSsl = settings.UseTls, Timeout = 15000 };
            if (!string.IsNullOrEmpty(settings.UserName))
            {
                client.Credentials = new NetworkCredential(settings.UserName, Unprotect(protection, settings.Password));
            }
            await client.SendMailAsync(message);
            return null;
        }
        catch (Exception exception) when (exception is SmtpException or InvalidOperationException or FormatException or CryptographicException)
        {
            await db.TryLogAsync(LogSeverity.Error, LogEntry.Mail, $"Échec d'envoi « {subject} » à {string.Join(", ", to)} : {exception.Message}", null, null);
            return exception.Message;
        }
    }

    public static string? Protect(IDataProtectionProvider protection, string? password) =>
        string.IsNullOrEmpty(password) ? null : protection.CreateProtector(ProtectionPurpose).Protect(password);

    private static string? Unprotect(IDataProtectionProvider protection, string? stored) =>
        string.IsNullOrEmpty(stored) ? null : protection.CreateProtector(ProtectionPurpose).Unprotect(stored);

    /// <summary>Lien absolu vers une page, si l'URL du site est renseignée dans les paramètres.</summary>
    public static string Link(string path) =>
        SettingsStore.Server.SiteUrl is { Length: > 0 } url ? $"{url.TrimEnd('/')}{path}" : path;
}
