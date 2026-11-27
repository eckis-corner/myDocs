namespace MyDocs.Shared.Tasks;

/// <summary>Arten von Vorgängen (11). Texte unter <c>TaskKind_{Art}</c>.</summary>
public static class TaskKinds
{
    public const string Review = "review";
    public const string Deadline = "deadline";
    public const string Task = "task";
    public const string Inbox = "inbox";
    public const string Cleanup = "cleanup";
    public const string MyLifeSync = "mylife-sync";

    /// <summary>Diese Arten lassen sich nur mit einem Ergebnis abschließen (15).</summary>
    public static IReadOnlySet<string> NeedOutcome { get; } = new HashSet<string>(StringComparer.Ordinal) { Review, Deadline };
}

/// <summary>Status eines Vorgangs (11). Texte unter <c>TaskState_{Status}</c>.</summary>
public static class TaskStates
{
    public const string Open = "open";
    public const string InProgress = "in-progress";
    public const string Waiting = "waiting";
    public const string Done = "done";
    public const string Discarded = "discarded";

    public static IReadOnlySet<string> Closed { get; } = new HashSet<string>(StringComparer.Ordinal) { Done, Discarded };

    public static IReadOnlyList<string> All { get; } = [Open, InProgress, Waiting, Done, Discarded];
}

/// <summary>Ergebnis einer Prüfung (15). Texte unter <c>TaskOutcome_{Ergebnis}</c>.</summary>
public static class TaskOutcomes
{
    public const string Unchanged = "unchanged";
    public const string Changed = "changed";
    public const string Cancelled = "cancelled";
    public const string Replaced = "replaced";

    public static IReadOnlyList<string> All { get; } = [Unchanged, Changed, Cancelled, Replaced];
}

public static class TaskPriorities
{
    public const string Normal = "normal";
    public const string High = "high";
}

public sealed record TaskDto(Guid Id, string Kind, string Title, string? Note, Guid? ResponsiblePersonId, DateOnly? DueOn, string Priority, string State,
    DateOnly? WaitUntil, string? Outcome, string? OutcomeNote, IReadOnlyList<Guid> ObjectIds, bool CanEdit, DateTimeOffset CreatedAt, string CreatedByName,
    DateTimeOffset? ClosedAt, string? ClosedByName, uint Version)
{
    /// <summary>Textschlüssel automatischer Vorgänge (<c>TaskTitle_{Schlüssel}</c>); der Leser sieht sie in seiner Sprache.</summary>
    public string? TitleKey { get; init; }
}

/// <summary>Neuer Vorgang von Hand oder Änderung der Angaben.</summary>
public sealed record TaskSaveRequest(string Title, string? Note, Guid? ResponsiblePersonId, DateOnly? DueOn, string Priority, IReadOnlyList<Guid> ObjectIds, uint Version = 0);

/// <summary>Statuswechsel: „wartet auf Dritte“ braucht <paramref name="WaitUntil"/>, Abschließen bei Prüfungen ein Ergebnis.</summary>
public sealed record TaskStateRequest(string State, DateOnly? WaitUntil, string? Outcome, string? OutcomeNote, uint Version);

/// <summary>Fehlercodes; Texte unter <c>TaskError_{Code}</c>.</summary>
public static class TaskErrors
{
    public const string NotFound = "not-found";
    public const string Forbidden = "forbidden";
    public const string TitleRequired = "title-required";
    public const string WaitUntilRequired = "wait-until-required";
    public const string OutcomeRequired = "outcome-required";
    public const string StateInvalid = "state-invalid";
    public const string Conflict = "conflict";
    public const string ResponsibleInvalid = "responsible-invalid";
}

/// <summary>Kommentar an einem Vorgang oder Objekt (50).</summary>
public sealed record CommentDto(Guid Id, string AuthorName, Guid AuthorId, DateTimeOffset At, string Text);

public sealed record CommentRequest(string Text);
