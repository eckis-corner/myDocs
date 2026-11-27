namespace MyDocs.Server.Data;

/// <summary>
/// Vorgang (11): Prüfung, Frist, Aufgabe, Posteingang, Aufräumen oder myLife-Abgleich. Automatische Vorgänge tragen
/// einen <see cref="TitleKey"/> (Text in der Sprache des Lesers) und einen <see cref="RuleKey"/>, damit dieselbe Regel
/// keinen zweiten offenen Vorgang anlegt.
/// </summary>
public sealed class WorkTask
{
    public Guid Id { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? TitleKey { get; set; }

    public string? RuleKey { get; set; }

    public string? Note { get; set; }

    public Guid? ResponsiblePersonId { get; set; }

    public DateOnly? DueOn { get; set; }

    public string Priority { get; set; } = "normal";

    public string State { get; set; } = "open";

    public DateOnly? WaitUntil { get; set; }

    public string? Outcome { get; set; }

    public string? OutcomeNote { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Wer angelegt hat; <see cref="Guid.Empty"/> für den Wiedervorlage-Dienst.</summary>
    public Guid CreatedBy { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public Guid? ClosedBy { get; set; }

    public uint Version { get; set; }
}

/// <summary>Zuordnung Vorgang ↔ Objekt (n:m).</summary>
public sealed class TaskObject
{
    public Guid TaskId { get; set; }

    public Guid ObjectId { get; set; }
}

/// <summary>Kommentar an einem Vorgang oder Objekt (50). Genau eines von beiden ist gesetzt.</summary>
public sealed class Comment
{
    public Guid Id { get; set; }

    public Guid? TaskId { get; set; }

    public Guid? ObjectId { get; set; }

    public Guid AuthorId { get; set; }

    public DateTimeOffset At { get; set; }

    public string Text { get; set; } = string.Empty;
}
