using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MyDocs.Server.Infrastructure.Settings;
using MyDocs.Shared.Admin;

namespace MyDocs.Server.Infrastructure.Mail;

/// <summary>Zugangsdaten für einen Versand, aus den Einstellungen mit entschlüsseltem Passwort.</summary>
public sealed record SmtpConfig(string Host, int Port, SmtpSecurity Security, string? Username, string? Password, string FromAddress, string FromName)
{
    public static async Task<SmtpConfig?> FromAsync(SettingsStore settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var stored = await settings.GetAsync(ct);
        if (!SettingsStore.IsMailConfigured(stored))
        {
            return null;
        }

        var smtp = stored.Smtp;
        return new SmtpConfig(smtp.Host, smtp.Port, smtp.Security, smtp.Username, settings.SmtpPassword(stored), smtp.FromAddress, smtp.FromName);
    }
}

/// <summary>Verschickt eine Mail. In den Tests ersetzt durch eine Attrappe, die nur mitschreibt.</summary>
public interface IMailTransport
{
    Task SendAsync(SmtpConfig config, MimeMessage message, CancellationToken ct);
}

/// <summary>Versand per SMTP mit MailKit.</summary>
public sealed class SmtpMailTransport : IMailTransport
{
    public async Task SendAsync(SmtpConfig config, MimeMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(config);
        using var client = new SmtpClient { Timeout = 30_000 };
        var security = config.Security switch
        {
            SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
            SmtpSecurity.None => SecureSocketOptions.None,
            _ => SecureSocketOptions.StartTlsWhenAvailable,
        };
        await client.ConnectAsync(config.Host, config.Port, security, ct);
        if (!string.IsNullOrEmpty(config.Username))
        {
            await client.AuthenticateAsync(config.Username, config.Password ?? string.Empty, ct);
        }

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(quit: true, ct);
    }

    public static MimeMessage Build(SmtpConfig config, string recipient, RenderedMail mail)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(mail);
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(config.FromName, config.FromAddress));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = mail.Subject;
        message.Body = new BodyBuilder { TextBody = mail.Text, HtmlBody = mail.Html }.ToMessageBody();
        return message;
    }
}
