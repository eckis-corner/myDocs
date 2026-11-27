namespace MyDocs.Server.Data;

/// <summary>
/// Eine Mail in der Outbox (wie myLife): gespeichert in derselben Transaktion wie ihr Anlass, verschickt im Hintergrund.
/// Die Parameter liegen mit Data Protection verschlüsselt und werden nach dem Versand gelöscht.
/// </summary>
public sealed class MailOutboxEntry
{
    public Guid Id { get; set; }

    public string Recipient { get; set; } = string.Empty;

    public string Language { get; set; } = "de";

    public string Template { get; set; } = string.Empty;

    public string? Parameters { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public DateTimeOffset? FailedAt { get; set; }
}
