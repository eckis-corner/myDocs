namespace MyDocs.Server.Data;

/// <summary>Verschlüsselte Datei im Volume (22); mehrere Versionen mit gleichem Inhalt teilen sich einen Blob.</summary>
public sealed class Blob
{
    /// <summary>HMAC-SHA-256 des Klartexts, hex; zugleich der Dateiname.</summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>Dateischlüssel, verpackt mit dem Server-Hauptschlüssel.</summary>
    public byte[] WrappedKey { get; set; } = [];

    public long Size { get; set; }

    public int RefCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Dokument mit Versionen (27), zugeordnet zu einem oder mehreren Objekten (21).</summary>
public sealed class Document
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public DateOnly? DocumentDate { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsDeleted { get; set; }

    /// <summary>Suchtext (25), beim Speichern berechnet; null heißt: beim nächsten Start neu berechnen.</summary>
    public string? SearchText { get; set; }

    /// <summary>Volltextindex aus <see cref="SearchText"/> (PostgreSQL, Konfiguration <c>german</c>), von der Datenbank berechnet.</summary>
    public NpgsqlTypes.NpgsqlTsVector? SearchVector { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>Erkannter Text der aktuellen Version (OCR bzw. Textebene, M3); null, solange nichts erkannt ist.</summary>
    public string? ContentText { get; set; }

    /// <summary>Dokumentart (<see cref="MyDocs.Shared.Documents.DocumentKinds"/>), etwa Rechnung oder Bescheid.</summary>
    public string? Kind { get; set; }

    /// <summary>Liegt im Posteingang (20): noch keinem Eintrag zugeordnet.</summary>
    public bool InInbox { get; set; }

    /// <summary>Wessen Posteingang: eine Person (privat) oder null für den Haushalt.</summary>
    public Guid? InboxPersonId { get; set; }

    /// <summary>Eingangskanal (<see cref="MyDocs.Shared.Documents.DocumentSources"/>).</summary>
    public string Source { get; set; } = MyDocs.Shared.Documents.DocumentSources.Upload;

    /// <summary>Herkunft in Worten, etwa Absender und Betreff einer Mail; ohne Inhalt.</summary>
    public string? SourceDetail { get; set; }

    /// <summary>Office-Datei mit Makros (49): markiert, nicht entfernt.</summary>
    public bool HasMacros { get; set; }
}

/// <summary>Übernommene Mail (148), damit sie nach einem Abbruch nicht zweimal in den Posteingang kommt.</summary>
public sealed class ProcessedMail
{
    /// <summary>SHA-256 der Message-ID (hex), ohne die Kennung selbst.</summary>
    public string Hash { get; set; } = string.Empty;

    public DateTimeOffset At { get; set; }
}

/// <summary>Unveränderliche Fassung eines Dokuments.</summary>
public sealed class DocumentVersion
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public int Number { get; set; }

    public string BlobHash { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = "application/octet-stream";

    public long Size { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid CreatedBy { get; set; }
}

/// <summary>Zuordnung Dokument ↔ Objekt (n:m, 21).</summary>
public sealed class DocumentObject
{
    public Guid DocumentId { get; set; }

    public Guid ObjectId { get; set; }
}
