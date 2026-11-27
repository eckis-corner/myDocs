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
