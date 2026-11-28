using MyDocs.Server.Data;
using MyDocs.Shared.Documents;
using MyDocs.Shared.Objects;

namespace MyDocs.Server.Infrastructure.Documents;

/// <summary>Aufbewahrung eines Dokuments (28, 174): von Hand gesetzt oder nach der Regel seiner Dokumentart.</summary>
internal static class Retention
{
    /// <summary>
    /// Bis wann aufbewahren: <see cref="RetentionRules.Forever"/> dauerhaft, null ohne Frist (keine Regel, oder ein
    /// zugeordneter Eintrag läuft noch). Abgelaufen ist die Frist am Tag danach.
    /// </summary>
    public static DateOnly? Until(Document document, IReadOnlyCollection<DocObject> linked)
    {
        if (document.RetainUntil is { } manual)
        {
            return manual;
        }

        if (RetentionRules.For(document.Kind) is not { } rule)
        {
            return null;
        }

        var date = document.DocumentDate ?? DateOnly.FromDateTime(document.CreatedAt.UtcDateTime);
        switch (rule.Basis)
        {
            case RetentionBasis.Forever:
                return RetentionRules.Forever;
            case RetentionBasis.DocumentDate:
                return date.AddYears(rule.Years);
            case RetentionBasis.EndOfYear:
                return new DateOnly(date.Year + rule.Years, 12, 31);
            case RetentionBasis.ObjectEnd:
                var live = linked.Where(o => !o.IsDeleted).ToList();
                if (live.Count == 0 || live.Any(o => o.LifecycleState is null || !Ended(o.LifecycleState)))
                {
                    return null;
                }

                return live.Max(o => o.LifecycleDate ?? DateOnly.FromDateTime(o.UpdatedAt.UtcDateTime)).AddYears(rule.Years);
            default:
                return null;
        }
    }

    public static bool IsDue(DateOnly? until, DateOnly today) => until is { } date && date != RetentionRules.Forever && date < today;

    private static bool Ended(string state) => LifecycleStates.Inactive.Contains(state) || state == LifecycleStates.Expired;
}
