namespace MyDocs.Server.Data;

/// <summary>
/// Verbindung eines Kontos mit myLife (56, 162): ein lesendes Token, nur als SHA-256 gespeichert, und der Stand des
/// letzten Abgleichs. Weicht der aktuelle Stand 30 Tage vom quittierten ab, entsteht der Vorgang „myLife abgleichen“ (60).
/// </summary>
public sealed class MyLifeLink
{
    public Guid UserId { get; set; }

    public byte[] TokenHash { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>Zuletzt quittierter Abgleich.</summary>
    public DateTimeOffset? LastSyncedAt { get; set; }

    /// <summary>Fingerabdruck des Stands, den myLife zuletzt übernommen hat.</summary>
    public string? SyncedFingerprint { get; set; }

    /// <summary>Seit wann der aktuelle Stand vom quittierten abweicht; null, solange beide gleich sind.</summary>
    public DateTimeOffset? PendingSince { get; set; }
}
