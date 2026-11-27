using MyDocs.Shared.Tasks;

namespace MyDocs.Shared.Dashboard;

/// <summary>Eine Frist in den nächsten 90 Tagen: Frist-Vorgang, Ablaufdatum oder Ende nach Kündigung.</summary>
public sealed record DeadlineItemDto(DateOnly Date, string Kind, Guid ObjectId, string ObjectTitle, string TypeKey, Guid? TaskId);

/// <summary>Monatliche Kosten eines Bereichs aus den regelmäßigen Zahlungen (Katalog: PaymentDefinition).</summary>
public sealed record PaymentAreaDto(int Area, decimal Monthly, int Count);

/// <summary>Ein Eintrag, dem etwas fehlt oder der lange nicht geprüft wurde.</summary>
public sealed record AttentionItemDto(Guid ObjectId, string Title, string TypeKey, IReadOnlyList<string> MissingFields, DateOnly? LastReviewedOn);

/// <summary>Startseite (65): fünf Blöcke, je nach Umschalter für mich oder den ganzen Haushalt.</summary>
public sealed record DashboardDto(
    IReadOnlyList<TaskDto> Overdue,
    IReadOnlyList<TaskDto> DueThisWeek,
    int InboxCount,
    IReadOnlyList<TaskDto> InboxOldest,
    IReadOnlyList<DeadlineItemDto> Deadlines,
    decimal MonthlyTotal,
    IReadOnlyList<PaymentAreaDto> PaymentsByArea,
    IReadOnlyList<AttentionItemDto> Incomplete,
    IReadOnlyList<AttentionItemDto> Unreviewed,
    int MyLifeSyncOpen)
{
    /// <summary>Alle Einträge mit fehlenden Angaben; die Liste zeigt nur die ersten.</summary>
    public int IncompleteCount { get; init; }

    /// <summary>Alle Einträge, die seit über 12 Monaten nicht geprüft sind.</summary>
    public int UnreviewedCount { get; init; }
}

/// <summary>Kennungen der Fristen für die Anzeige (<c>DeadlineKind_{Art}</c>).</summary>
public static class DeadlineKinds
{
    public const string Task = "task";
    public const string Expiry = "expiry";
    public const string ContractEnd = "contract-end";
}
