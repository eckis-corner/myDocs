using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Auth;
using MyDocs.Server.Infrastructure.Mail;
using MyDocs.Server.Infrastructure.Objects;
using MyDocs.Server.Infrastructure.Settings;
using MyDocs.Server.Infrastructure.Tasks;
using MyDocs.Shared.Household;
using MyDocs.Shared.Tasks;

namespace MyDocs.Server.Infrastructure.Notifications;

/// <summary>
/// Benachrichtigungen (14, 47): je Person eine tägliche Zusammenfassung zur gewählten Stunde, sofort nur bei
/// Eskalationsstufe 3 an alle Mitglieder, die den Vorgang sehen dürfen. Mails nennen Titel von Vorgang und Eintrag,
/// nie Feldwerte oder Dokumente; Push-Nachrichten nur Zahlen. Ohne Mail und ntfy passiert nichts.
/// </summary>
public sealed partial class Notifier(
    MyDocsDbContext db, SettingsStore settings, MailOutbox outbox, IPushSender push, TaskAccess tasks, TimeProvider time, ILogger<Notifier> logger)
{
    private sealed record Recipient(AppUser User, Caller Caller);

    private sealed record Line(WorkTask Task, string Text);

    /// <summary>Schickt fällige Zusammenfassungen; liefert die Zahl der Personen, die eine bekommen haben.</summary>
    public async Task<int> SendDigestsAsync(CancellationToken ct)
    {
        var stored = await settings.GetAsync(ct);
        var mail = SettingsStore.IsMailConfigured(stored);
        var ntfy = SettingsStore.IsNtfyConfigured(stored);
        if (!mail && !ntfy)
        {
            return 0;
        }

        var now = time.GetUtcNow();
        var local = TimeZoneInfo.ConvertTime(now, time.LocalTimeZone);
        var localDay = DateOnly.FromDateTime(local.DateTime);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var sent = 0;
        foreach (var recipient in await RecipientsAsync(membersOnly: false, ct))
        {
            var user = recipient.User;
            if (user.DigestHour is not { } hour || local.Hour < hour || user.LastDigestOn == localDay)
            {
                continue;
            }

            user.LastDigestOn = localDay;
            var horizon = today.AddDays(7);
            var open = await tasks.Visible(recipient.Caller)
                .Where(t => (t.State == TaskStates.Open || t.State == TaskStates.InProgress) && t.DueOn != null && t.DueOn <= horizon)
                .OrderBy(t => t.DueOn).ToListAsync(ct);
            var me = recipient.Caller.PersonId;
            var mine = open.Where(t => t.ResponsiblePersonId == me || (recipient.Caller.IsMember && t.ResponsiblePersonId == null)).ToList();
            // Nach 7 Tagen geht ein Vorgang an weitere Haushaltsmitglieder (14).
            var others = recipient.Caller.IsMember ? open.Where(t => t.ResponsiblePersonId != null && t.ResponsiblePersonId != me && t.EscalationLevel >= 2).ToList() : [];
            if (mine.Count + others.Count == 0)
            {
                continue;
            }

            var overdue = mine.Where(t => t.DueOn < today).ToList();
            var upcoming = mine.Where(t => t.DueOn >= today).ToList();
            var language = user.Language;
            var sections = new List<string>();
            await AddSectionAsync(sections, "Mail_digest_overdue", overdue, language, ct);
            await AddSectionAsync(sections, "Mail_digest_upcoming", upcoming, language, ct);
            await AddSectionAsync(sections, "Mail_digest_others", others, language, ct);

            if (mail)
            {
                outbox.Enqueue(user, MailTemplate.Digest, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["list"] = string.Join("\n\n", sections),
                    ["count"] = (mine.Count + others.Count).ToString(CultureInfo.InvariantCulture),
                    ["link"] = Link(stored.PublicUrl, "tasks"),
                });
            }

            await PushAsync(user, stored.PublicUrl, "Push_digest_title", "Push_digest_body", mine.Count + others.Count, overdue.Count + others.Count, upcoming.Count, ct);
            sent++;
        }

        await db.SaveChangesAsync(ct);
        return sent;
    }

    /// <summary>Stufe 3 erreicht: sofort an alle Mitglieder, die den Vorgang sehen dürfen.</summary>
    public async Task UrgentAsync(IReadOnlyCollection<Guid> taskIds, CancellationToken ct)
    {
        if (taskIds.Count == 0)
        {
            return;
        }

        var stored = await settings.GetAsync(ct);
        var mail = SettingsStore.IsMailConfigured(stored);
        if (!mail && !SettingsStore.IsNtfyConfigured(stored))
        {
            return;
        }

        foreach (var recipient in await RecipientsAsync(membersOnly: true, ct))
        {
            var visible = await tasks.Visible(recipient.Caller).Where(t => taskIds.Contains(t.Id)).OrderBy(t => t.DueOn).ToListAsync(ct);
            if (visible.Count == 0)
            {
                continue;
            }

            if (mail)
            {
                var lines = await LinesAsync(visible, recipient.User.Language, ct);
                outbox.Enqueue(recipient.User, MailTemplate.Urgent, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["list"] = string.Join("\n", lines),
                    ["count"] = visible.Count.ToString(CultureInfo.InvariantCulture),
                    ["link"] = Link(stored.PublicUrl, "tasks"),
                });
            }

            await PushAsync(recipient.User, stored.PublicUrl, "Push_urgent_title", "Push_urgent_body", visible.Count, visible.Count, 0, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    internal static string Link(string? publicUrl, string path) => publicUrl is null ? string.Empty : $"{publicUrl}/{path}";

    private async Task<List<Recipient>> RecipientsAsync(bool membersOnly, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var users = await db.Users.Where(u => u.TwoFactorEnabled && u.Email != null && (u.LockoutEnd == null || u.LockoutEnd < now)).ToListAsync(ct);
        var memberRole = await db.Roles.Where(r => r.Name == HouseholdRoles.Member).Select(r => r.Id).FirstOrDefaultAsync(ct);
        var members = (await db.UserRoles.Where(r => r.RoleId == memberRole).Select(r => r.UserId).ToListAsync(ct)).ToHashSet();
        var persons = await db.Persons.Where(p => p.UserId != null).ToDictionaryAsync(p => p.UserId!.Value, p => p.Id, ct);
        return [.. users.Where(u => !membersOnly || members.Contains(u.Id))
            .Select(u => new Recipient(u, new Caller(u.Id, persons.TryGetValue(u.Id, out var person) ? person : null, members.Contains(u.Id))))];
    }

    private async Task AddSectionAsync(List<string> sections, string headingKey, List<WorkTask> items, string language, CancellationToken ct)
    {
        if (items.Count > 0)
        {
            sections.Add(MailTexts.Phrase(headingKey, language) + "\n" + string.Join("\n", await LinesAsync(items, language, ct)));
        }
    }

    /// <summary>„• 01.11.2026 Haftpflicht: Prüfen, ob alles noch stimmt“, in der Sprache der Mail.</summary>
    private async Task<List<string>> LinesAsync(List<WorkTask> items, string language, CancellationToken ct)
    {
        var ids = items.Select(t => t.Id).ToList();
        var titles = await db.TaskObjects.Where(l => ids.Contains(l.TaskId)).Join(db.Objects, l => l.ObjectId, o => o.Id, (l, o) => new { l.TaskId, o.Title })
            .ToListAsync(ct);
        var culture = MailTexts.Culture(language);
        return [.. items.Select(t =>
        {
            var title = t.TitleKey is { } key ? MailTexts.Phrase($"TaskTitle_{key}", language) ?? t.Title : t.Title;
            var objectTitle = titles.FirstOrDefault(x => x.TaskId == t.Id)?.Title;
            var date = t.DueOn?.ToString("d", culture) ?? string.Empty;
            return objectTitle is null ? $"• {date} {title}" : $"• {date} {objectTitle}: {title}";
        })];
    }

    private async Task PushAsync(AppUser user, string? publicUrl, string titleKey, string bodyKey, int count, int overdue, int upcoming, CancellationToken ct)
    {
        if (user.NtfyTopic is not { } topic || !SettingsStore.IsNtfyConfigured(await settings.GetAsync(ct)))
        {
            return;
        }

        var culture = MailTexts.Culture(user.Language);
        var title = string.Format(culture, MailTexts.Phrase(titleKey, user.Language) ?? titleKey, count);
        var body = string.Format(culture, MailTexts.Phrase(bodyKey, user.Language) ?? bodyKey, overdue, upcoming);
        try
        {
            await push.SendAsync(topic, title, body, publicUrl is null ? null : Link(publicUrl, "tasks"), ct);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            LogPushFailed(logger, user.Id, exception.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Push an {UserId} nicht verschickt: {Error}")]
    private static partial void LogPushFailed(ILogger logger, Guid userId, string error);
}

/// <summary>Prüft alle 5 Minuten, wer seine tägliche Zusammenfassung bekommt.</summary>
public sealed partial class NotificationService(IServiceScopeFactory scopes, TimeProvider time, ILogger<NotificationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<Notifier>().SendDigestsAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException or Npgsql.NpgsqlException)
            {
                LogFailed(logger, exception);
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Tägliche Zusammenfassung fehlgeschlagen.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
