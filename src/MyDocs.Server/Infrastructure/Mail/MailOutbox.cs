using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Audit;
using MyDocs.Server.Infrastructure.Settings;
using MyDocs.Shared.Audit;

namespace MyDocs.Server.Infrastructure.Mail;

/// <summary>Weckt den Versanddienst, sobald eine neue Mail gespeichert ist.</summary>
public sealed class MailSignal : IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Schon geweckt.
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);

    public void Dispose() => _signal.Dispose();
}

/// <summary>
/// Legt Mails in die Outbox. Gespeichert wird mit dem nächsten <c>SaveChanges</c> des Aufrufers, also in derselben
/// Transaktion wie ihr Anlass; danach startet der Versand.
/// </summary>
public sealed class MailOutbox
{
    internal const string ParametersPurpose = "MyDocs.Mail.Outbox.v1";
    private readonly MyDocsDbContext _db;
    private readonly IDataProtectionProvider _protection;
    private readonly TimeProvider _time;
    private bool _pending;

    public MailOutbox(MyDocsDbContext db, IDataProtectionProvider protection, TimeProvider time, MailSignal signal)
    {
        _db = db;
        _protection = protection;
        _time = time;
        db.SavedChanges += (_, _) =>
        {
            if (_pending)
            {
                _pending = false;
                signal.Notify();
            }
        };
    }

    public Guid Enqueue(string recipient, string language, string template, IReadOnlyDictionary<string, string> parameters)
    {
        var id = Guid.CreateVersion7();
        _db.MailOutbox.Add(new MailOutboxEntry
        {
            Id = id,
            Recipient = recipient,
            Language = language,
            Template = template,
            Parameters = _protection.CreateProtector(ParametersPurpose).Protect(JsonSerializer.Serialize(parameters)),
            CreatedAt = _time.GetUtcNow(),
            NextAttemptAt = _time.GetUtcNow(),
        });
        _pending = true;
        return id;
    }

    public Guid Enqueue(AppUser user, string template, IReadOnlyDictionary<string, string>? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(user);
        var all = new Dictionary<string, string>(parameters ?? new Dictionary<string, string>(), StringComparer.Ordinal) { ["name"] = user.DisplayName };
        return Enqueue(user.Email!, user.Language, template, all);
    }
}

/// <summary>
/// Verschickt die Outbox im Hintergrund: sofort nach neuen Mails, sonst alle 30 Sekunden. Fehlgeschlagene Mails werden
/// mit wachsendem Abstand wiederholt (1 Minute bis 1 Tag, gut zwei Tage lang) und danach aufgegeben, mit Eintrag im
/// Protokoll. Ohne SMTP-Einstellungen bleiben Mails liegen, bis der Administrator den Versand einrichtet.
/// </summary>
public sealed partial class MailOutboxService(
    IServiceScopeFactory scopes, IMailTransport transport, MailSignal signal, IDataProtectionProvider protection,
    TimeProvider time, ILogger<MailOutboxService> logger) : BackgroundService
{
    internal const string NotConfigured = "not-configured";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private static readonly int[] BackoffMinutes = [1, 5, 15, 30, 60, 120, 240, 480, 720, 1440];
    private readonly SemaphoreSlim _gate = new(1, 1);

    public static int MaxAttempts => BackoffMinutes.Length;

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
            catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException or Npgsql.NpgsqlException)
            {
                LogProcessingFailed(logger, exception);
            }

            try
            {
                await signal.WaitAsync(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Verschickt alle fälligen Mails einmal; liefert die Zahl der verschickten.</summary>
    public async Task<int> ProcessOnceAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MyDocsDbContext>();
            var settings = scope.ServiceProvider.GetRequiredService<SettingsStore>();
            var audit = scope.ServiceProvider.GetRequiredService<AuditLog>();
            var now = time.GetUtcNow();
            var due = await db.MailOutbox
                .Where(m => m.SentAt == null && m.FailedAt == null && m.NextAttemptAt <= now)
                .OrderBy(m => m.NextAttemptAt).Take(20).ToListAsync(ct);
            if (due.Count == 0)
            {
                return 0;
            }

            var config = await SmtpConfig.FromAsync(settings, ct);
            if (config is null)
            {
                foreach (var entry in due)
                {
                    entry.LastError = NotConfigured;
                    entry.NextAttemptAt = now.AddMinutes(5);
                }

                await db.SaveChangesAsync(ct);
                return 0;
            }

            var sent = 0;
            foreach (var entry in due)
            {
                try
                {
                    var mail = MailTexts.Render(entry.Template, entry.Language, ReadParameters(entry));
                    await transport.SendAsync(config, SmtpMailTransport.Build(config, entry.Recipient, mail), ct);
                    entry.SentAt = time.GetUtcNow();
                    entry.Parameters = null;
                    entry.LastError = null;
                    sent++;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    Failed(audit, entry, exception);
                }
            }

            await db.SaveChangesAsync(ct);
            return sent;
        }
        finally
        {
            _gate.Release();
        }
    }

    private Dictionary<string, string> ReadParameters(MailOutboxEntry entry) =>
        entry.Parameters is null ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(protection.CreateProtector(MailOutbox.ParametersPurpose).Unprotect(entry.Parameters)) ?? [];

    private void Failed(AuditLog audit, MailOutboxEntry entry, Exception exception)
    {
        entry.Attempts++;
        entry.LastError = exception.Message.Length > 300 ? exception.Message[..300] : exception.Message;
        // Ohne Schlüsselring lassen sich die Parameter nie mehr lesen: sofort aufgeben.
        if (entry.Attempts >= MaxAttempts || exception is CryptographicException)
        {
            entry.FailedAt = time.GetUtcNow();
            entry.Parameters = null;
            audit.Add(AuditActions.MailFailed, title: entry.Template, details: entry.LastError);
        }
        else
        {
            entry.NextAttemptAt = time.GetUtcNow().AddMinutes(BackoffMinutes[entry.Attempts - 1]);
        }

        LogSendFailed(logger, entry.Id, entry.Attempts, exception.GetType().Name);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mail-Outbox konnte nicht verarbeitet werden")]
    private static partial void LogProcessingFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mail {MailId} nicht verschickt (Versuch {Attempt}, {Error})")]
    private static partial void LogSendFailed(ILogger logger, Guid mailId, int attempt, string error);
}
