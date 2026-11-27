namespace MyDocs.Shared.Documents;

public sealed record DocumentVersionDto(int Number, string FileName, string ContentType, long Size, DateTimeOffset CreatedAt, string CreatedByName);

/// <summary>Dokument mit allen Versionen (neueste zuerst), seinen Objekten und den Rechten des Aufrufers.</summary>
public sealed record DocumentDto(Guid Id, string Title, DateOnly? DocumentDate, string? Note, IReadOnlyList<DocumentVersionDto> Versions,
    IReadOnlyList<Guid> ObjectIds, bool CanEdit, DateTimeOffset UpdatedAt, DateTimeOffset? DeletedAt)
{
    public DocumentVersionDto Current => Versions[0];
}

public sealed record DocumentUpdateRequest(string Title, DateOnly? DocumentDate, string? Note);

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
