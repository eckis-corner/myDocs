using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MyCommon.Catalog;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Objects;
using MyDocs.Server.Infrastructure.Tasks;
using MyDocs.Shared.Dashboard;
using MyDocs.Shared.Objects;
using MyDocs.Shared.Tasks;

namespace MyDocs.Server.Api;

/// <summary>
/// Startseite (65): überfällig und diese Woche fällig, Posteingang, Fristen der nächsten 90 Tage, Zahlungen je Monat
/// und Zustand des Bestands. „Meins“ heißt: ich bin zuständig oder Eigentümer. Die Rechte gelten wie überall.
/// </summary>
internal static class DashboardEndpoints
{
    private const int ListLimit = 20;

    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dashboard", GetAsync).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> GetAsync(string? scope, ClaimsPrincipal principal, ObjectAccess access, TaskAccess tasks, MyDocsDbContext db, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        var mine = scope != "household";
        var today = access.Today;
        var weekEnd = today.AddDays(7 - (((int)today.DayOfWeek + 6) % 7) - 1);

        // Vorgänge
        var taskQuery = tasks.Visible(caller).Where(t => t.State != TaskStates.Done && t.State != TaskStates.Discarded && t.State != TaskStates.Waiting);
        if (mine)
        {
            taskQuery = taskQuery.Where(t => t.ResponsiblePersonId == caller.PersonId);
        }

        var open = await taskQuery.ToListAsync(ct);
        var overdue = open.Where(t => t.DueOn < today).OrderBy(t => t.DueOn).Take(ListLimit).ToList();
        var thisWeek = open.Where(t => t.DueOn >= today && t.DueOn <= weekEnd).OrderBy(t => t.DueOn).Take(ListLimit).ToList();
        var inbox = open.Where(t => t.Kind == TaskKinds.Inbox).OrderBy(t => t.CreatedAt).ToList();

        // Einträge
        var objects = (await access.Visible(caller).ToListAsync(ct)).Where(ReviewPlan.IsActive).ToList();
        if (mine)
        {
            objects = [.. objects.Where(o => o.ResponsiblePersonId == caller.PersonId || o.OwnerPersonId == caller.PersonId)];
        }

        var titles = objects.ToDictionary(o => o.Id);
        var deadlines = new List<DeadlineItemDto>();
        var horizon = today.AddDays(90);
        foreach (var task in open.Where(t => t.Kind == TaskKinds.Deadline && t.DueOn >= today && t.DueOn <= horizon))
        {
            var objectId = await db.TaskObjects.Where(l => l.TaskId == task.Id).Select(l => l.ObjectId).FirstOrDefaultAsync(ct);
            if (titles.TryGetValue(objectId, out var item))
            {
                deadlines.Add(new DeadlineItemDto(task.DueOn!.Value, DeadlineKinds.Task, item.Id, item.Title, item.TypeKey, task.Id));
            }
        }

        foreach (var item in objects)
        {
            if (item.ExpiresOn is { } expires && expires >= today && expires <= horizon)
            {
                deadlines.Add(new DeadlineItemDto(expires, DeadlineKinds.Expiry, item.Id, item.Title, item.TypeKey, null));
            }

            if (item.LifecycleState == LifecycleStates.Cancelled && item.LifecycleDate is { } end && end >= today && end <= horizon)
            {
                deadlines.Add(new DeadlineItemDto(end, DeadlineKinds.ContractEnd, item.Id, item.Title, item.TypeKey, null));
            }
        }

        // Zahlungen je Monat aus den regelmäßigen Zahlungen des Katalogs (wie die Übersicht in myLife, 62).
        var payments = new Dictionary<int, (decimal Monthly, int Count)>();
        foreach (var item in objects.Where(o => o.LifecycleState != LifecycleStates.Draft))
        {
            if (Monthly(item) is not { } monthly || ObjectCatalog.Find(item.TypeKey) is not { } type)
            {
                continue;
            }

            var current = payments.GetValueOrDefault(type.Area);
            payments[type.Area] = (current.Monthly + monthly, current.Count + 1);
        }

        // Zustand des Bestands
        var incomplete = new List<AttentionItemDto>();
        var unreviewed = new List<AttentionItemDto>();
        var yearAgo = today.AddMonths(-12);
        foreach (var item in objects.OrderBy(o => o.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            if (ObjectCatalog.Find(item.TypeKey) is not { } type)
            {
                continue;
            }

            var values = new ObjectData { Fields = item.Fields };
            var missing = type.Fields.Where(f => (f.Required || f.Recommended) && FieldValues.IsVisible(f, values) && values.Get(f.Key).Length == 0).Select(f => f.Key).ToList();
            if (missing.Count > 0)
            {
                incomplete.Add(new AttentionItemDto(item.Id, item.Title, item.TypeKey, missing, item.LastReviewedOn));
            }

            if (ReviewPlan.IntervalMonths(item) is > 0 && (item.LastReviewedOn ?? DateOnly.FromDateTime(item.CreatedAt.UtcDateTime)) < yearAgo)
            {
                unreviewed.Add(new AttentionItemDto(item.Id, item.Title, item.TypeKey, [], item.LastReviewedOn));
            }
        }

        var myLifeSync = open.Count(t => t.Kind == TaskKinds.MyLifeSync);
        var dtos = await TaskEndpoints.ToDtosAsync([.. overdue.Concat(thisWeek).Concat(inbox.Take(5)).DistinctBy(t => t.Id)], caller, tasks, db, ct);
        var byId = dtos.ToDictionary(d => d.Id);
        return TypedResults.Ok(new DashboardDto(
            [.. overdue.Select(t => byId[t.Id])],
            [.. thisWeek.Select(t => byId[t.Id])],
            inbox.Count,
            [.. inbox.Take(5).Select(t => byId[t.Id])],
            [.. deadlines.OrderBy(d => d.Date).Take(ListLimit)],
            payments.Values.Sum(p => p.Monthly),
            [.. payments.OrderBy(p => p.Key).Select(p => new PaymentAreaDto(p.Key, Math.Round(p.Value.Monthly, 2), p.Value.Count))],
            [.. incomplete.Take(ListLimit)],
            [.. unreviewed.Take(ListLimit)],
            myLifeSync)
        {
            IncompleteCount = incomplete.Count,
            UnreviewedCount = unreviewed.Count,
        });
    }

    /// <summary>Monatlicher Betrag einer regelmäßigen Zahlung (Katalog: PaymentDefinition); einmalige Zahlungen zählen nicht.</summary>
    internal static decimal? Monthly(DocObject item)
    {
        if (ObjectCatalog.Find(item.TypeKey)?.Payment is not { } payment)
        {
            return null;
        }

        var values = new ObjectData { Fields = item.Fields };
        if (payment.When is { } condition && !condition.Values.Contains(values.Get(condition.FieldKey)))
        {
            return null;
        }

        if (!FieldValues.TryNumber(values.Get(payment.AmountField), out var amount) || amount <= 0)
        {
            return null;
        }

        var frequency = payment.FixedFrequency ?? (payment.FrequencyField is { } field ? values.Get(field) : null);
        return frequency switch
        {
            "monthly" => amount,
            "quarterly" => amount / 3m,
            "halfYearly" => amount / 6m,
            "yearly" => amount / 12m,
            _ => null,
        };
    }
}
