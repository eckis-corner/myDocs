namespace MyDocs.Shared.MyLife;

/// <summary>
/// Brückenformat Version 1 (164, <c>docs/brueckenformat.md</c>): was myLife mit dem lesenden Token abholt. myLife hat
/// eigene Klassen für dasselbe JSON; geändert wird nur durch Ergänzen.
/// </summary>
public sealed record BridgeExport(
    string Format,
    int Version,
    DateTimeOffset GeneratedAt,
    string Fingerprint,
    BridgeInstance Instance,
    BridgePerson Person,
    IReadOnlyList<BridgeObject> Objects)
{
    public const string FormatName = "mydocs-bridge";

    public const int CurrentVersion = 1;
}

/// <summary>Die Instanz, von der der Stand kommt (für die Anzeige und den Eintrag „myDocs-Wiederherstellung“).</summary>
public sealed record BridgeInstance(string Url);

/// <summary>Die Person, deren Einträge übertragen werden.</summary>
public sealed record BridgePerson(Guid Id, string Name);

/// <summary>
/// Ein Eintrag: Katalogfelder in kanonischer Form, Ablageorte (Feld → ID eines mitgelieferten Ablageorts), Lebenszyklus
/// und Stand der Prüfung (57). <see cref="Ended"/> fasst die Zustände zusammen, die in myLife „beendet“ heißen (29).
/// Verweise auf Einträge, die nicht mitkommen, sind entfernt; der Name eines Ablageorts bleibt im Feld.
/// </summary>
public sealed record BridgeObject(
    Guid Id,
    string Type,
    int SchemaVersion,
    string Title,
    SortedDictionary<string, string> Fields,
    SortedDictionary<string, Guid> Locations,
    DateOnly? ExpiresOn,
    string? LifecycleState,
    DateOnly? LifecycleDate,
    bool Ended,
    DateOnly? LastReviewedOn,
    DateOnly? OverdueSince,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<BridgeDocument> Documents);

/// <summary>Ein markiertes Dokument (58): die aktuelle Version. <see cref="Hash"/> ändert sich mit jedem neuen Inhalt.</summary>
public sealed record BridgeDocument(Guid Id, string Hash, string Title, string FileName, string ContentType, long Size, DateOnly? Date);

/// <summary>Quittung nach dem Abgleich: der Fingerabdruck des Stands, den myLife übernommen hat.</summary>
public sealed record BridgeSyncedRequest(string Fingerprint);

/// <summary>Stand der Verbindung für die Seite „myLife“ in myDocs.</summary>
public sealed record MyLifeLinkDto(
    bool Connected,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? LastSyncedAt,
    bool Pending,
    DateTimeOffset? PendingSince,
    IReadOnlyList<MyLifeObjectDto> Objects,
    string? MyLifeUrl,
    bool HasPerson);

/// <summary>Ein Eintrag, der an myLife geht, mit der Zahl seiner Dokumente.</summary>
public sealed record MyLifeObjectDto(Guid Id, string TypeKey, string Title, int Documents);

/// <summary>Neues Token: wird genau einmal gezeigt. <see cref="ConnectUrl"/> öffnet myLife mit Adresse und Token im Fragment.</summary>
public sealed record MyLifeConnectResult(string Token, string ServerUrl, string? ConnectUrl);

/// <summary>Markierung „In myLife führen“ bzw. „nach myLife“ setzen.</summary>
public sealed record MyLifeFlagRequest(bool Enabled);

public static class MyLifeRules
{
    /// <summary>Präfix des lesenden Tokens, damit es sich nie mit einem Gerätetoken verwechseln lässt.</summary>
    public const string TokenPrefix = "mdl_";

    /// <summary>Größte Datei, die myLife annimmt (58).</summary>
    public const long MaxDocumentBytes = 25L * 1024 * 1024;

    /// <summary>Nach so vielen Tagen mit offenen Änderungen entsteht der Vorgang „myLife abgleichen“ (60).</summary>
    public const int SyncDueDays = 30;
}

/// <summary>Fehlercodes der Brücke; Texte unter <c>MyLifeError_{Code}</c>.</summary>
public static class MyLifeErrors
{
    public const string NoPerson = "no-person";
    public const string TooLarge = "too-large";
    public const string InvalidToken = "invalid-token";
    public const string InvalidFingerprint = "invalid-fingerprint";
}
