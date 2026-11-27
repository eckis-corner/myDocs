namespace MyDocs.Server.Data;

/// <summary>
/// Eintrag im Audit-Log (35). <see cref="OwnerPersonId"/> ist die Person, deren private Daten betroffen sind; sie sieht
/// den Eintrag in ihrem Protokoll (31). IP gekürzt, Details ohne Inhalte.
/// </summary>
public sealed class AuditEntry
{
    public long Id { get; set; }

    public DateTimeOffset At { get; set; }

    public Guid? ActorId { get; set; }

    public Guid? OwnerPersonId { get; set; }

    public string Action { get; set; } = string.Empty;

    public Guid? ObjectId { get; set; }

    public Guid? DocumentId { get; set; }

    /// <summary>Titel zum Zeitpunkt des Eintrags, damit das Protokoll auch nach dem Löschen lesbar bleibt.</summary>
    public string? Title { get; set; }

    public string? Ip { get; set; }

    public string? Details { get; set; }
}
