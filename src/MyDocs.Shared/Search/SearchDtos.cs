namespace MyDocs.Shared.Search;

public static class SearchKinds
{
    public const string Entry = "object";
    public const string Document = "document";
}

/// <summary>Treffer der Suche: ein Objekt oder ein Dokument; bei Dokumenten das Objekt, über das man es öffnet.</summary>
public sealed record SearchHitDto(string Kind, Guid Id, string Title, string? TypeKey, Guid? ObjectId, string? ObjectTitle, string? Snippet);
