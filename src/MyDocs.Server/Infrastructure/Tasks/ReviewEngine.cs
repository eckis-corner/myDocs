using Microsoft.EntityFrameworkCore;
using MyDocs.Server.Data;
using MyDocs.Shared.Objects;
using MyDocs.Shared.Tasks;

namespace MyDocs.Server.Infrastructure.Tasks;

/// <summary>
/// Ein Durchlauf der Wiedervorlage (13–16): Prüfungen und Fristen anlegen, Ablaufdaten in den Lebenszyklus
/// übernehmen, „wartet auf Dritte“ wieder vorlegen, Eskalation fortschreiben und „überfällig“ setzen. Jeder Schritt ist
/// idempotent; ein zweiter Durchlauf am selben Tag ändert nichts.
/// </summary>
public sealed class ReviewEngine(MyDocsDbContext db, Notifications.Notifier notifier, MyLife.MyLifeBridge bridge, TimeProvider time)
{
    /// <summary>Was ein Durchlauf getan hat (für Tests und das Protokoll des Containers).</summary>
    public sealed record Result(int Reviews, int Deadlines, int Lifecycle, int Resumed, int Escalated, int Overdue, int Inbox = 0, int MyLife = 0, int Cleanup = 0);

    public async Task<Result> RunAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        int reviews = 0, deadlines = 0, lifecycle = 0, resumed = 0, escalated = 0, overdue = 0, inboxTasks = 0, myLife = 0, cleanup = 0;

        var objects = (await db.Objects.Where(o => !o.IsDeleted).ToListAsync(ct)).Where(ReviewPlan.IsActive).ToList();
        var openRules = (await db.Tasks.Where(t => t.RuleKey != null && t.State != TaskStates.Done && t.State != TaskStates.Discarded).Select(t => t.RuleKey!).ToListAsync(ct)).ToHashSet();
        var allRules = (await db.Tasks.Where(t => t.RuleKey != null && t.RuleKey.StartsWith("deadline:")).Select(t => t.RuleKey!).ToListAsync(ct)).ToHashSet();

        foreach (var item in objects)
        {
            // Prüfung fällig: genau ein offener Vorgang „Prüfung“ je Objekt.
            if (ReviewPlan.NextReview(item) is { } next && next <= today && openRules.Add($"review:{item.Id}"))
            {
                Add(item, TaskKinds.Review, "review", "Prüfen, ob alles noch stimmt", $"review:{item.Id}", next, now);
                reviews++;
            }

            // Frist aus Feldern: einmal je Termin, auch wenn der Vorgang schon erledigt ist.
            if (ReviewPlan.Deadline(item, today) is { } deadline && today >= deadline.Date.AddDays(-deadline.LeadDays) && deadline.Date >= today)
            {
                var rule = $"deadline:{item.Id}:{deadline.Key}:{deadline.Date:yyyy-MM-dd}";
                if (allRules.Add(rule))
                {
                    Add(item, TaskKinds.Deadline, deadline.Key, deadline.Key == "loan-end" ? "Kredit läuft aus: Anschluss klären" : "Kündigen oder verlängern?", rule, deadline.Date, now);
                    deadlines++;
                }
            }

            // Ablaufdatum: gültig → läuft ab → abgelaufen (nur Muster „Gültigkeit“, 9).
            if (Lifecycle.PatternOf(item.TypeKey) == LifecyclePattern.Validity && item.ExpiresOn is { } expires)
            {
                var target = expires <= today ? LifecycleStates.Expired
                    : expires.AddDays(-ReviewRules.ExpiringLeadDays) <= today ? LifecycleStates.Expiring
                    : null;
                if (target is not null && item.LifecycleState != target
                    && (item.LifecycleState == LifecycleStates.Valid || (item.LifecycleState == LifecycleStates.Expiring && target == LifecycleStates.Expired)))
                {
                    db.ObjectHistory.Add(new ObjectHistoryEntry { ObjectId = item.Id, At = now, ActorId = Guid.Empty, Field = "lifecycle", OldValue = item.LifecycleState, NewValue = target });
                    item.LifecycleState = target;
                    FollowUps.Create(db, item, target, today, now, Guid.Empty);
                    lifecycle++;
                }
            }
        }

        // „Wartet auf Dritte“ kommt am Datum wieder (11).
        foreach (var task in await db.Tasks.Where(t => t.State == TaskStates.Waiting && t.WaitUntil != null && t.WaitUntil <= today).ToListAsync(ct))
        {
            task.State = TaskStates.Open;
            task.WaitUntil = null;
            task.EscalationLevel = 0;
            db.Comments.Add(new Comment { Id = Guid.CreateVersion7(), TaskId = task.Id, AuthorId = Guid.Empty, At = now, Text = "Wieder vorgelegt: Die Wartezeit ist um." });
            resumed++;
        }

        // Posteingang (20, 143): Liegt das älteste Dokument länger als 7 Tage, gibt es je Posteingang einen Vorgang;
        // ist er leer, schließt der Dienst ihn wieder.
        var inboxes = await db.Documents.Where(d => d.InInbox && !d.IsDeleted)
            .GroupBy(d => d.InboxPersonId).Select(g => new { Person = g.Key, Oldest = g.Min(d => d.CreatedAt) }).ToListAsync(ct);
        foreach (var inbox in inboxes)
        {
            var due = DateOnly.FromDateTime(inbox.Oldest.UtcDateTime).AddDays(ReviewRules.InboxDueDays);
            var rule = $"inbox:{inbox.Person?.ToString() ?? "household"}";
            if (due <= today && openRules.Add(rule))
            {
                db.Tasks.Add(new WorkTask
                {
                    Id = Guid.CreateVersion7(),
                    Kind = TaskKinds.Inbox,
                    Title = "Posteingang abarbeiten",
                    TitleKey = "inbox",
                    RuleKey = rule,
                    ResponsiblePersonId = inbox.Person,
                    DueOn = due,
                    CreatedAt = now,
                    CreatedBy = Guid.Empty,
                });
                inboxTasks++;
            }
        }

        var waiting = inboxes.Select(i => $"inbox:{i.Person?.ToString() ?? "household"}").ToHashSet();
        foreach (var task in await db.Tasks.Where(t => t.Kind == TaskKinds.Inbox && t.RuleKey != null && t.State != TaskStates.Done && t.State != TaskStates.Discarded).ToListAsync(ct))
        {
            if (!waiting.Contains(task.RuleKey!))
            {
                task.State = TaskStates.Done;
                task.ClosedAt = now;
                task.ClosedBy = Guid.Empty;
                inboxTasks++;
            }
        }

        // myLife (60, 165): Weicht der Stand seit 30 Tagen vom zuletzt übernommenen ab, ein Vorgang je Person. Die
        // Quittung nach dem Abgleich schließt ihn.
        foreach (var (link, person) in await bridge.RefreshPendingAsync(ct))
        {
            var rule = $"mylife:{person.Id}";
            if (openRules.Add(rule))
            {
                db.Tasks.Add(new WorkTask
                {
                    Id = Guid.CreateVersion7(),
                    Kind = TaskKinds.MyLifeSync,
                    Title = "myLife abgleichen",
                    TitleKey = "mylife-sync",
                    RuleKey = rule,
                    ResponsiblePersonId = person.Id,
                    DueOn = DateOnly.FromDateTime(link.PendingSince!.Value.UtcDateTime).AddDays(MyDocs.Shared.MyLife.MyLifeRules.SyncDueDays),
                    CreatedAt = now,
                    CreatedBy = Guid.Empty,
                });
                myLife++;
            }
        }

        // Aufräumen (28, 174): Ist die Aufbewahrung eines Dokuments abgelaufen, gibt es je Person einen Vorgang „Aufräumen“
        // (wer für den Eintrag zuständig ist bzw. ihn besitzt); sind alle erledigt, schließt der Dienst ihn wieder.
        var documents = await db.Documents.Where(d => !d.IsDeleted && !d.InInbox && (d.Kind != null || d.RetainUntil != null)).ToListAsync(ct);
        var documentLinks = await Api.CleanupEndpoints.LinkedAsync(db, [.. documents.Select(d => d.Id)], ct);
        var cleanupDue = new Dictionary<Guid, DateOnly>();
        foreach (var document in documents)
        {
            var items = documentLinks.GetValueOrDefault(document.Id) ?? [];
            var until = Documents.Retention.Until(document, items);
            if (!Documents.Retention.IsDue(until, today) || items.Where(o => !o.IsDeleted).Select(o => o.OwnerPersonId ?? o.ResponsiblePersonId).FirstOrDefault(p => p is not null) is not { } person)
            {
                continue;
            }

            var due = until!.Value.AddDays(1);
            cleanupDue[person] = cleanupDue.TryGetValue(person, out var earlier) && earlier < due ? earlier : due;
        }

        foreach (var (person, due) in cleanupDue)
        {
            if (openRules.Add($"cleanup:{person}"))
            {
                db.Tasks.Add(new WorkTask
                {
                    Id = Guid.CreateVersion7(),
                    Kind = TaskKinds.Cleanup,
                    Title = "Aufräumen",
                    TitleKey = "cleanup",
                    RuleKey = $"cleanup:{person}",
                    ResponsiblePersonId = person,
                    DueOn = due < today ? today : due,
                    CreatedAt = now,
                    CreatedBy = Guid.Empty,
                });
                cleanup++;
            }
        }

        foreach (var task in await db.Tasks.Where(t => t.Kind == TaskKinds.Cleanup && t.RuleKey != null && t.State != TaskStates.Done && t.State != TaskStates.Discarded).ToListAsync(ct))
        {
            if (task.ResponsiblePersonId is not { } person || !cleanupDue.ContainsKey(person))
            {
                task.State = TaskStates.Done;
                task.ClosedAt = now;
                task.ClosedBy = Guid.Empty;
                cleanup++;
            }
        }

        // Eskalation (14): Stufe 1 am Fälligkeitstag, 2 nach 7 Tagen, 3 nach 14 Tagen; bei Prüfungen danach „überfällig“.
        var open = await db.Tasks.Where(t => (t.State == TaskStates.Open || t.State == TaskStates.InProgress) && t.DueOn != null && t.DueOn <= today).ToListAsync(ct);
        var urgent = new List<Guid>();
        foreach (var task in open)
        {
            var days = today.DayNumber - task.DueOn!.Value.DayNumber;
            var level = days >= 14 ? 3 : days >= 7 ? 2 : 1;
            if (level > task.EscalationLevel)
            {
                task.EscalationLevel = level;
                escalated++;
                if (level == 3)
                {
                    urgent.Add(task.Id);
                }
            }

            if (level == 3 && task.Kind == TaskKinds.Review)
            {
                var linked = await db.TaskObjects.Where(l => l.TaskId == task.Id).Select(l => l.ObjectId).ToListAsync(ct);
                foreach (var item in objects.Where(o => linked.Contains(o.Id) && o.OverdueSince is null))
                {
                    item.OverdueSince = task.DueOn.Value.AddDays(14);
                    overdue++;
                }
            }
        }

        await db.SaveChangesAsync(ct);
        // Sofort nur Stufe 3 (47); alles andere bringt die tägliche Zusammenfassung.
        await notifier.UrgentAsync(urgent, ct);
        return new Result(reviews, deadlines, lifecycle, resumed, escalated, overdue, inboxTasks, myLife, cleanup);
    }

    private void Add(DocObject item, string kind, string key, string germanTitle, string rule, DateOnly due, DateTimeOffset now)
    {
        var task = new WorkTask
        {
            Id = Guid.CreateVersion7(),
            Kind = kind,
            Title = germanTitle,
            TitleKey = key,
            RuleKey = rule,
            ResponsiblePersonId = item.ResponsiblePersonId ?? item.OwnerPersonId,
            DueOn = due,
            CreatedAt = now,
            CreatedBy = Guid.Empty,
        };
        db.Tasks.Add(task);
        db.TaskObjects.Add(new TaskObject { TaskId = task.Id, ObjectId = item.Id });
    }
}

/// <summary>Führt die Wiedervorlage alle 15 Minuten aus (wie der Notfalldienst von myLife: idempotent, übersteht Neustarts).</summary>
public sealed partial class ReviewService(IServiceScopeFactory scopes, TimeProvider time, ILogger<ReviewService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<ReviewEngine>().RunAsync(stoppingToken);
                if (result.Reviews + result.Deadlines + result.Lifecycle + result.Resumed + result.Escalated + result.Overdue + result.Inbox + result.MyLife + result.Cleanup > 0)
                {
                    LogRun(logger, result.Reviews, result.Deadlines, result.Lifecycle, result.Escalated);
                }
            }
            catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException or Npgsql.NpgsqlException)
            {
                LogFailed(logger, exception);
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(15), time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Wiedervorlage: {Reviews} Prüfungen, {Deadlines} Fristen, {Lifecycle} Zustände, {Escalated} Eskalationen.")]
    private static partial void LogRun(ILogger logger, int reviews, int deadlines, int lifecycle, int escalated);

    [LoggerMessage(Level = LogLevel.Error, Message = "Wiedervorlage fehlgeschlagen.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
