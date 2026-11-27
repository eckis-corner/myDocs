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
public sealed class ReviewEngine(MyDocsDbContext db, TimeProvider time)
{
    /// <summary>Was ein Durchlauf getan hat (für Tests und das Protokoll des Containers).</summary>
    public sealed record Result(int Reviews, int Deadlines, int Lifecycle, int Resumed, int Escalated, int Overdue);

    public async Task<Result> RunAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        int reviews = 0, deadlines = 0, lifecycle = 0, resumed = 0, escalated = 0, overdue = 0;

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

        // Eskalation (14): Stufe 1 am Fälligkeitstag, 2 nach 7 Tagen, 3 nach 14 Tagen; bei Prüfungen danach „überfällig“.
        var open = await db.Tasks.Where(t => (t.State == TaskStates.Open || t.State == TaskStates.InProgress) && t.DueOn != null && t.DueOn <= today).ToListAsync(ct);
        foreach (var task in open)
        {
            var days = today.DayNumber - task.DueOn!.Value.DayNumber;
            var level = days >= 14 ? 3 : days >= 7 ? 2 : 1;
            if (level > task.EscalationLevel)
            {
                task.EscalationLevel = level;
                escalated++;
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
        return new Result(reviews, deadlines, lifecycle, resumed, escalated, overdue);
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
                if (result.Reviews + result.Deadlines + result.Lifecycle + result.Resumed + result.Escalated + result.Overdue > 0)
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
