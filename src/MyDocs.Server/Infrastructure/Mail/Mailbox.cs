using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using MyDocs.Server.Infrastructure.Settings;
using MyDocs.Shared.Admin;

namespace MyDocs.Server.Infrastructure.Mail;

/// <summary>Zugang zum IMAP-Postfach, aus den Einstellungen (Passwort entschlüsselt erst hier).</summary>
public sealed record ImapConfig(string Host, int Port, SmtpSecurity Security, string Username, string? Password, string Folder, string? TrustedAuthServId)
{
    public static async Task<ImapConfig?> FromAsync(SettingsStore settings, CancellationToken ct)
    {
        var stored = await settings.GetAsync(ct);
        return SettingsStore.IsImapConfigured(stored)
            ? new ImapConfig(stored.Imap.Host, stored.Imap.Port, stored.Imap.Security, stored.Imap.Username!, settings.ImapPassword(stored), stored.Imap.Folder, stored.Imap.TrustedAuthServId)
            : null;
    }
}

/// <summary>Eine Mail im Postfach: Kennung für das Verschieben und Größe, bevor sie geladen wird.</summary>
public sealed record MailSummary(string Id, long Size);

/// <summary>Geöffnetes Postfach für einen Durchlauf.</summary>
public interface IMailboxSession : IAsyncDisposable
{
    /// <summary>Höchstens <paramref name="limit"/> Mails im Eingangsordner, älteste zuerst.</summary>
    Task<IReadOnlyList<MailSummary>> ListAsync(int limit, CancellationToken ct);

    Task<MimeMessage> GetAsync(string id, CancellationToken ct);

    /// <summary>Verschiebt die Mail in einen Ordner neben dem Eingangsordner; legt ihn bei Bedarf an.</summary>
    Task MoveAsync(string id, string folder, CancellationToken ct);
}

/// <summary>Öffnet das Postfach; in den Tests eine Attrappe.</summary>
public interface IMailboxConnector
{
    Task<IMailboxSession> OpenAsync(ImapConfig config, CancellationToken ct);
}

/// <summary>IMAP über MailKit. Jeder Durchlauf verbindet sich neu, das übersteht Neustarts des Mailservers.</summary>
public sealed class ImapMailboxConnector : IMailboxConnector
{
    public async Task<IMailboxSession> OpenAsync(ImapConfig config, CancellationToken ct)
    {
        var client = new ImapClient { Timeout = 60_000 };
        try
        {
            var options = config.Security switch
            {
                SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
                SmtpSecurity.None => SecureSocketOptions.None,
                _ => SecureSocketOptions.StartTls,
            };
            await client.ConnectAsync(config.Host, config.Port, options, ct);
            await client.AuthenticateAsync(config.Username, config.Password ?? string.Empty, ct);
            var inbox = string.Equals(config.Folder, "INBOX", StringComparison.OrdinalIgnoreCase) ? client.Inbox : await client.GetFolderAsync(config.Folder, ct);
            await inbox.OpenAsync(FolderAccess.ReadWrite, ct);
            return new Session(client, inbox);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private sealed class Session(ImapClient client, IMailFolder inbox) : IMailboxSession
    {
        public async Task<IReadOnlyList<MailSummary>> ListAsync(int limit, CancellationToken ct)
        {
            var ids = await inbox.SearchAsync(SearchQuery.All, ct);
            if (ids.Count == 0)
            {
                return [];
            }

            var summaries = await inbox.FetchAsync([.. ids.Take(limit)], MessageSummaryItems.UniqueId | MessageSummaryItems.Size, ct);
            return [.. summaries.Select(s => new MailSummary(s.UniqueId.ToString(), (long)(s.Size ?? 0)))];
        }

        public Task<MimeMessage> GetAsync(string id, CancellationToken ct) => inbox.GetMessageAsync(UniqueId.Parse(id), ct);

        public async Task MoveAsync(string id, string folder, CancellationToken ct)
        {
            var parent = inbox.ParentFolder ?? client.GetFolder(client.PersonalNamespaces[0]);
            IMailFolder? target;
            try
            {
                target = await parent.GetSubfolderAsync(folder, ct);
            }
            catch (FolderNotFoundException)
            {
                target = await parent.CreateAsync(folder, isMessageFolder: true, ct);
            }

            if (target is null)
            {
                throw new FolderNotFoundException(folder);
            }

            await inbox.MoveToAsync(UniqueId.Parse(id), target, ct);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (client.IsConnected)
                {
                    await client.DisconnectAsync(quit: true);
                }
            }
            finally
            {
                client.Dispose();
            }
        }
    }
}
