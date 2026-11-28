namespace MyDocs.Shared.Documents;

public sealed record DocumentVersionDto(int Number, string FileName, string ContentType, long Size, DateTimeOffset CreatedAt, string CreatedByName)
{
    /// <summary>Stand der Verarbeitung im Worker (<see cref="ProcessingStates"/>).</summary>
    public string Processing { get; init; } = ProcessingStates.Pending;

    /// <summary>
    /// Typ der Vorschau im Browser: <c>application/pdf</c> für das PDF aus dem Worker, sonst der Typ des Originals,
    /// wenn er in <see cref="Previews.InlineTypes"/> steht; null ohne Vorschau.
    /// </summary>
    public string? PreviewType { get; init; }

    public bool HasThumbnail { get; init; }
}

/// <summary>Stand der Verarbeitung (24): wartet, fertig (auch wenn ein Schritt nicht ging) oder aufgegeben.</summary>
public static class ProcessingStates
{
    public const string Pending = "pending";
    public const string Done = "done";
    public const string Failed = "failed";
}

/// <summary>Dokument mit allen Versionen (neueste zuerst), seinen Objekten und den Rechten des Aufrufers.</summary>
public sealed record DocumentDto(Guid Id, string Title, DateOnly? DocumentDate, string? Note, IReadOnlyList<DocumentVersionDto> Versions,
    IReadOnlyList<Guid> ObjectIds, bool CanEdit, DateTimeOffset UpdatedAt, DateTimeOffset? DeletedAt)
{
    public DocumentVersionDto Current => Versions[0];

    /// <summary>Dokumentart (<see cref="DocumentKinds"/>).</summary>
    public string? Kind { get; init; }
}

public sealed record DocumentUpdateRequest(string Title, DateOnly? DocumentDate, string? Note)
{
    /// <summary>Dokumentart; null lässt sie leer.</summary>
    public string? Kind { get; init; }
}

/// <summary>Dokumentarten (20); Texte unter <c>DocumentKind_{Schlüssel}</c>. Später hängen Aufbewahrungsfristen daran (28).</summary>
public static class DocumentKinds
{
    public static IReadOnlyList<string> All { get; } =
    [
        "invoice", "letter", "contract", "policy", "notice", "statement", "cancellation", "certificate", "receipt", "payslip", "other",
    ];

    public static bool IsValid(string? kind) => kind is null || All.Contains(kind);
}

/// <summary>Kamera-Upload (23): so viele Seiten höchstens in einem Dokument.</summary>
public static class CameraLimits
{
    public const int MaxPages = 50;
}

/// <summary>Eingangskanäle (19); Texte unter <c>DocumentSource_{Schlüssel}</c>.</summary>
public static class DocumentSources
{
    public const string Upload = "upload";
    public const string Camera = "camera";
    public const string Scan = "scan";
    public const string Mail = "mail";
}

/// <summary>Ein Dokument im Posteingang (20).</summary>
public sealed record InboxItemDto(Guid Id, string Title, string? Kind, DateOnly? DocumentDate, string Source, string? SourceDetail,
    bool Household, DateTimeOffset CreatedAt, DocumentVersionDto Current, DateTimeOffset? DeletedAt);

/// <summary>
/// Zuordnen aus dem Posteingang: Angaben, mindestens ein Eintrag und auf Wunsch ein Vorgang dazu (zuständig ist,
/// wer zuordnet).
/// </summary>
public sealed record InboxAssignRequest(string Title, string? Kind, DateOnly? DocumentDate, IReadOnlyList<Guid> ObjectIds, string? TaskTitle, DateOnly? TaskDueOn);

/// <summary>In den Posteingang des Haushalts geben oder ihn selbst übernehmen.</summary>
public sealed record InboxOwnerRequest(bool Household);

/// <summary>Zuordnung zu Objekten (21); mindestens eines.</summary>
public sealed record DocumentObjectsRequest(IReadOnlyList<Guid> ObjectIds);

/// <summary>Fehlercodes; Texte unter <c>DocumentError_{Code}</c>.</summary>
public static class DocumentErrors
{
    public const string TooLarge = "too-large";
    public const string NoFile = "no-file";
    public const string NotFound = "not-found";
    public const string Forbidden = "forbidden";
    public const string NeedsObject = "needs-object";
    public const string TitleRequired = "title-required";
    public const string InvalidKind = "invalid-kind";
    public const string TaskTitleRequired = "task-title-required";
    public const string InvalidImage = "invalid-image";
    public const string TooManyPages = "too-many-pages";
}

/// <summary>Was der Browser direkt anzeigen darf. Alles andere nur als Download, vor allem kein SVG und kein HTML.</summary>
public static class Previews
{
    public static IReadOnlySet<string> InlineTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf", "image/png", "image/jpeg", "image/gif", "image/webp",
    };

    public static bool IsImage(string contentType) => contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && InlineTypes.Contains(contentType);
}
