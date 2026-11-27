using MyDocs.Server.Data;
using MyDocs.Shared.Objects;

namespace MyDocs.Server.Infrastructure.Tasks;

/// <summary>Berechnet für ein Objekt Intervall, nächsten Prüftermin und Frist (13).</summary>
public static class ReviewPlan
{
    public static int? IntervalMonths(DocObject item) => item.ReviewIntervalMonths ?? ReviewRules.DefaultIntervalMonths(item.TypeKey);

    /// <summary>Nächste Prüfung: fester Termin, sonst letzte Prüfung (bzw. Anlage) plus Intervall; null ohne Prüfung.</summary>
    public static DateOnly? NextReview(DocObject item)
    {
        if (item.NextReviewOn is { } fixedDate)
        {
            return fixedDate;
        }

        if (IntervalMonths(item) is not ({ } months and > 0))
        {
            return null;
        }

        var from = item.LastReviewedOn ?? DateOnly.FromDateTime(item.CreatedAt.UtcDateTime);
        return from.AddMonths(months);
    }

    public static ReviewRules.Deadline? Deadline(DocObject item, DateOnly today) =>
        ReviewRules.DeadlineFor(item.TypeKey, new ObjectData { Fields = item.Fields, ExpiresOn = item.ExpiresOn }, today);

    public static bool IsActive(DocObject item) => !item.IsDeleted && (item.LifecycleState is null || !LifecycleStates.Inactive.Contains(item.LifecycleState));

    public static ReviewInfoDto? Info(DocObject item, DateOnly today)
    {
        var next = NextReview(item);
        var deadline = Deadline(item, today);
        if (next is null && deadline is null && item.LastReviewedOn is null && item.OverdueSince is null && item.ReviewIntervalMonths is null)
        {
            return null;
        }

        return new ReviewInfoDto(IntervalMonths(item), item.ReviewIntervalMonths is not null, next, item.NextReviewOn is not null, item.LastReviewedOn,
            item.OverdueSince, deadline?.Date, deadline?.Key);
    }
}
