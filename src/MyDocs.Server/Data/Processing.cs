namespace MyDocs.Server.Data;

/// <summary>
/// Auftrag an den Worker (24, 137): einer je Inhalt (Blob), nicht je Version. Gleiche Dateien werden nur einmal
/// verarbeitet. Zustände in <see cref="MyDocs.Shared.Documents.ProcessingStates"/>.
/// </summary>
public sealed class ProcessingJob
{
    public string SourceHash { get; set; } = string.Empty;

    /// <summary>Dateiname der ersten Version mit diesem Inhalt; die Endung hilft beim Erkennen von Office-Dateien.</summary>
    public string FileName { get; set; } = string.Empty;

    public string State { get; set; } = MyDocs.Shared.Documents.ProcessingStates.Pending;

    /// <summary>Erkannte Art (<see cref="Infrastructure.Processing.FileKind"/>), sobald der Auftrag lief.</summary>
    public string? Kind { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    /// <summary>Letzter Fehler, ohne Inhalt der Datei.</summary>
    public string? Error { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>
/// Ableitung eines Inhalts: PDF mit Textebene bzw. PDF-Vorschau von Office-Dateien, Vorschaubild. Selbst ein
/// verschlüsselter Blob; das Original bleibt immer erhalten (24).
/// </summary>
public sealed class BlobDerivative
{
    public string SourceHash { get; set; } = string.Empty;

    /// <summary><see cref="DerivativeKinds"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    public string BlobHash { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long Size { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public static class DerivativeKinds
{
    public const string Pdf = "pdf";
    public const string Thumbnail = "thumbnail";
}

/// <summary>Erkannter Text eines Inhalts (pdftotext bzw. OCR), gekürzt auf <see cref="Infrastructure.Processing.DocumentProcessor.MaxTextLength"/> Zeichen.</summary>
public sealed class BlobText
{
    public string SourceHash { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}
