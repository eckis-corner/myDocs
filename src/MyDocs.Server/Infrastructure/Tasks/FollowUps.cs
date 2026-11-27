using MyDocs.Server.Data;
using MyDocs.Shared.Objects;
using MyDocs.Shared.Tasks;

namespace MyDocs.Server.Infrastructure.Tasks;

/// <summary>
/// Folgevorgänge beim Wechsel im Lebenszyklus (10), fest im Code statt in der Oberfläche einstellbar (8). Jeder trägt
/// einen Textschlüssel (<c>TaskTitle_{Schlüssel}</c>) und einen Regelschlüssel gegen doppelte offene Vorgänge.
/// </summary>
public static class FollowUps
{
    public sealed record FollowUp(string Key, string Kind, int DueInDays, string GermanTitle);

    private static readonly Dictionary<string, FollowUp[]> ByState = new(StringComparer.Ordinal)
    {
        [LifecycleStates.Cancelled] = [new("cancellation-confirmation", TaskKinds.Task, 14, "Kündigungsbestätigung ablegen")],
        [LifecycleStates.Ended] =
        [
            new("final-invoice", TaskKinds.Task, 30, "Letzte Abrechnung prüfen"),
            new("direct-debit", TaskKinds.Task, 30, "Lastschrift prüfen"),
        ],
        [LifecycleStates.Expiring] = [new("renew-or-replace", TaskKinds.Deadline, 0, "Verlängern oder ersetzen?")],
        [LifecycleStates.Expired] = [new("create-replacement", TaskKinds.Task, 7, "Ersatz anlegen")],
        [LifecycleStates.Disposed] = [new("check-related-contracts", TaskKinds.Task, 14, "Zugehörige Verträge prüfen")],
    };

    public static IReadOnlyList<FollowUp> For(string state) => ByState.GetValueOrDefault(state) ?? [];

    /// <summary>Legt die Folgevorgänge an; ein noch offener Vorgang derselben Regel am selben Objekt wird nicht verdoppelt.</summary>
    public static IReadOnlyList<WorkTask> Create(MyDocsDbContext db, DocObject item, string state, DateOnly today, DateTimeOffset now, Guid actor,
        IReadOnlyCollection<Guid>? alsoLinked = null)
    {
        var created = new List<WorkTask>();
        foreach (var followUp in For(state))
        {
            var rule = $"{followUp.Key}:{item.Id}";
            if (db.Tasks.Any(t => t.RuleKey == rule && t.State != TaskStates.Done && t.State != TaskStates.Discarded))
            {
                continue;
            }

            var task = new WorkTask
            {
                Id = Guid.CreateVersion7(),
                Kind = followUp.Kind,
                Title = followUp.GermanTitle,
                TitleKey = followUp.Key,
                RuleKey = rule,
                ResponsiblePersonId = item.ResponsiblePersonId ?? item.OwnerPersonId,
                DueOn = today.AddDays(followUp.DueInDays),
                CreatedAt = now,
                CreatedBy = actor,
            };
            db.Tasks.Add(task);
            db.TaskObjects.Add(new TaskObject { TaskId = task.Id, ObjectId = item.Id });
            foreach (var other in alsoLinked ?? [])
            {
                db.TaskObjects.Add(new TaskObject { TaskId = task.Id, ObjectId = other });
            }

            created.Add(task);
        }

        return created;
    }
}
