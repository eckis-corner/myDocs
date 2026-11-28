using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MimeKit;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Audit;
using MyDocs.Server.Infrastructure.Files;
using MyDocs.Server.Infrastructure.Mail;
using MyDocs.Server.Infrastructure.Processing;
using MyDocs.Server.Infrastructure.Settings;
using MyDocs.Shared.Audit;
using MyDocs.Shared.Documents;
using MyDocs.Shared.Household;

namespace MyDocs.Server.Infrastructure.Inbox;

/// <summary>
/// Mail-Eingang (49, 148–151): holt alle 2 Minuten die Mails aus dem IMAP-Postfach. Nur Mitglieder mit hinterlegter
/// Absenderadresse und bestandener Prüfung (SPF/DKIM/DMARC) kommen durch; ihre Anhänge aus der Positivliste landen im
/// eigenen Posteingang. Danach liegt die Mail im Ordner „myDocs-verarbeitet“ bzw. „myDocs-abgelehnt“.
/// </summary>
public sealed partial class MailIntakeService(IServiceScopeFactory scopes, IMailboxConnector connector, SettingsStore settings,
    IOptions<MyDocsOptions> options, TimeProvider time, ILogger<MailIntakeService> logger) : BackgroundService
{
    public const string ProcessedFolder = "myDocs-verarbeitet";
    public const string RejectedFolder = "myDocs-abgelehnt";

    /// <summary>Größere Mails werden nicht geladen (die App hat 512 MB Speicher); übliche Mailserver lassen ohnehin weniger zu.</summary>
    public const long MaxMessageBytes = 50L * 1024 * 1024;

    private const int BatchSize = 20;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(2);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public override void Dispose()
    {
        _gate.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Mailserver weg, Passwort falsch, Netz: im nächsten Durchlauf wieder.
                LogFailed(logger, exception.GetType().Name, exception.Message);
            }

            try
            {
                await Task.Delay(PollInterval, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Ein Durchlauf; liefert die Zahl angenommener Mails.</summary>
    public async Task<int> ProcessOnceAsync(CancellationToken ct)
    {
        if (await ImapConfig.FromAsync(settings, ct) is not { } config)
        {
            return 0;
        }

        await _gate.WaitAsync(ct);
        try
        {
            await using var session = await connector.OpenAsync(config, ct);
            var accepted = 0;
            foreach (var summary in await session.ListAsync(BatchSize, ct))
            {
                await using var scope = scopes.CreateAsyncScope();
                var ok = await HandleAsync(scope.ServiceProvider, session, summary, config, ct);
                await session.MoveAsync(summary.Id, ok ? ProcessedFolder : RejectedFolder, ct);
                accepted += ok ? 1 : 0;
            }

            return accepted;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Öffnet das Postfach einmal (Knopf „Testen“ in den Einstellungen); liefert die Zahl wartender Mails.</summary>
    public async Task<int> TestAsync(CancellationToken ct)
    {
        var config = await ImapConfig.FromAsync(settings, ct) ?? throw new InvalidOperationException("IMAP ist nicht eingerichtet.");
        await using var session = await connector.OpenAsync(config, ct);
        return (await session.ListAsync(1000, ct)).Count;
    }

    private async Task<bool> HandleAsync(IServiceProvider services, IMailboxSession session, MailSummary summary, ImapConfig config, CancellationToken ct)
    {
        var db = services.GetRequiredService<MyDocsDbContext>();
        var audit = services.GetRequiredService<AuditLog>();
        if (summary.Size > MaxMessageBytes)
        {
            return await RejectAsync(db, audit, null, "?", $"größer als {MaxMessageBytes / 1024 / 1024} MB", ct);
        }

        var message = await session.GetAsync(summary.Id, ct);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(message.MessageId ?? $"{message.From}|{message.Date:O}|{message.Subject}")));
        if (await db.ProcessedMails.AnyAsync(m => m.Hash == hash, ct))
        {
            // Schon übernommen, nur das Verschieben ging beim letzten Mal nicht mehr.
            return true;
        }

        if (message.From.Mailboxes.ToList() is not [var from] || string.IsNullOrWhiteSpace(from.Address))
        {
            return await RejectAsync(db, audit, null, message.From.ToString(), "kein eindeutiger Absender", ct);
        }

        var address = from.Address.Trim().ToLowerInvariant();
        var normalized = address.ToUpperInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct)
            ?? await db.Users.FirstOrDefaultAsync(u => u.MailSenders.Contains(address), ct);
        var person = user is null ? null : await db.Persons.Where(p => p.UserId == user.Id).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
        if (user is null || person is null)
        {
            return await RejectAsync(db, audit, null, address, "unbekannter Absender", ct);
        }

        var memberRole = await db.Roles.Where(r => r.Name == HouseholdRoles.Member).Select(r => r.Id).FirstOrDefaultAsync(ct);
        if (user.LockoutEnd > time.GetUtcNow() || !await db.UserRoles.AnyAsync(r => r.UserId == user.Id && r.RoleId == memberRole, ct))
        {
            return await RejectAsync(db, audit, person, address, "kein aktives Mitglied", ct);
        }

        var verdict = SenderCheck.Verify(message, address, config.TrustedAuthServId);
        if (!verdict.Passed)
        {
            return await RejectAsync(db, audit, person, address, verdict.Reason, ct);
        }

        var blobs = services.GetRequiredService<BlobStore>();
        var processing = services.GetRequiredService<ProcessingScheduler>();
        var maxBytes = options.Value.MaxFileSizeMegabytes * 1024L * 1024L;
        var subject = string.IsNullOrWhiteSpace(message.Subject) ? null : message.Subject.Trim();
        var detail = subject is null ? address : $"{address} · {subject}";
        var now = time.GetUtcNow();
        var taken = 0;
        var skipped = new List<string>();
        foreach (var part in MailAttachments.Candidates(message))
        {
            var name = Path.GetFileName(part.FileName ?? "Anhang");
            using var buffer = new MemoryStream();
            if (part.Content is null)
            {
                continue;
            }

            await part.Content.DecodeToAsync(buffer, ct);
            var content = buffer.ToArray();
            if (content.Length == 0 || content.Length > maxBytes || !MailAttachments.Accept(name, content.AsSpan(0, Math.Min(content.Length, FileKinds.HeaderLength))))
            {
                skipped.Add(name);
                continue;
            }

            var (blob, size) = await blobs.StoreAsync(new MemoryStream(content), maxBytes, ct);
            var document = await InboxIntake.AddAsync(db, processing, audit, blob, size, name, MailAttachments.ContentType(name, part.ContentType.MimeType),
                person, DocumentSources.Mail, detail, user.Id, now, ct);
            document.HasMacros = MacroCheck.HasMacros(content, name);
            taken++;
        }

        // Ohne brauchbaren Anhang (oder auf Wunsch immer) wird der Mailtext zum Dokument; die Vorschau macht der Worker.
        if (taken == 0 || user.MailBodyAlways)
        {
            var body = MailText.From(message);
            if (body.Length > 0)
            {
                var text = new StringBuilder()
                    .Append("Von: ").AppendLine(from.ToString())
                    .Append("An: ").AppendLine(message.To.ToString())
                    .Append("Datum: ").AppendLine(message.Date.ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture))
                    .Append("Betreff: ").AppendLine(subject ?? string.Empty)
                    .AppendLine()
                    .Append(body)
                    .ToString();
                var bytes = Encoding.UTF8.GetBytes(text);
                if (bytes.Length <= maxBytes)
                {
                    var (blob, size) = await blobs.StoreAsync(new MemoryStream(bytes), maxBytes, ct);
                    await InboxIntake.AddAsync(db, processing, audit, blob, size, FileName(subject) + ".txt", "text/plain", person, DocumentSources.Mail, detail, user.Id, now, ct,
                        title: subject ?? $"Mail von {address}");
                    taken++;
                }
            }
        }

        db.ProcessedMails.Add(new ProcessedMail { Hash = hash, At = now });
        await db.SaveChangesAsync(ct);
        LogAccepted(logger, taken, skipped.Count);
        return true;
    }

    private async Task<bool> RejectAsync(MyDocsDbContext db, AuditLog audit, Guid? person, string sender, string reason, CancellationToken ct)
    {
        // Steht im Protokoll der Person, wenn der Absender zu ihr gehört, sonst nur im Protokoll des Administrators.
        audit.Add(AuditActions.MailRejected, Guid.Empty, person, details: $"{sender}: {reason}");
        await db.SaveChangesAsync(ct);
        LogRejected(logger, reason);
        return false;
    }

    private static string FileName(string? subject)
    {
        var name = new string([.. (subject ?? "Mail").Where(c => !char.IsControl(c) && !Path.GetInvalidFileNameChars().Contains(c) && c is not ('/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|'))]).Trim();
        return name.Length == 0 ? "Mail" : name[..Math.Min(name.Length, 100)];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Mail angenommen: {Documents} Dokumente, {Skipped} Anhänge übergangen.")]
    private static partial void LogAccepted(ILogger logger, int documents, int skipped);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mail abgelehnt: {Reason}.")]
    private static partial void LogRejected(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mail-Eingang: {Type}: {Message}")]
    private static partial void LogFailed(ILogger logger, string type, string message);
}
