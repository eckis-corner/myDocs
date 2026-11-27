namespace MyDocs.Server.Data;

/// <summary>
/// Objekt aus dem Katalog (6): Versicherung, Konto, Vertrag … Gehört dem Haushalt (<see cref="OwnerPersonId"/> null)
/// oder einer Person (4, 34). Felder liegen als JSON in kanonischer Form, damit neue Felder keine Migration brauchen.
/// </summary>
public sealed class DocObject
{
    public Guid Id { get; set; }

    public string TypeKey { get; set; } = string.Empty;

    public int SchemaVersion { get; set; } = 1;

    public string Title { get; set; } = string.Empty;

    public Dictionary<string, string> Fields { get; set; } = [];

    public Dictionary<string, Guid> Locations { get; set; } = [];

    public string? Notes { get; set; }

    public DateOnly? ExpiresOn { get; set; }

    /// <summary>Eigentümer; null heißt Haushalt.</summary>
    public Guid? OwnerPersonId { get; set; }

    /// <summary>Zuständige Person (12); bei Haushaltsobjekten Pflicht.</summary>
    public Guid? ResponsiblePersonId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>Suchtext (25), beim Speichern berechnet; null heißt: beim nächsten Start neu berechnen.</summary>
    public string? SearchText { get; set; }

    /// <summary>Volltextindex aus <see cref="SearchText"/> (PostgreSQL, Konfiguration <c>german</c>), von der Datenbank berechnet.</summary>
    public NpgsqlTypes.NpgsqlTsVector? SearchVector { get; set; }

    /// <summary>Zeilenversion von PostgreSQL (<c>xmin</c>) gegen gleichzeitiges Überschreiben.</summary>
    public uint Version { get; set; }
}

/// <summary>Freigabe eines privaten Objekts an ein Konto (33).</summary>
public sealed class ObjectShare
{
    public Guid ObjectId { get; set; }

    public Guid UserId { get; set; }

    public bool CanEdit { get; set; }
}

/// <summary>Änderung eines Felds (27). Feld ist der Feldschlüssel oder ein fester Name wie <c>title</c>, <c>owner</c>.</summary>
public sealed class ObjectHistoryEntry
{
    public long Id { get; set; }

    public Guid ObjectId { get; set; }

    public DateTimeOffset At { get; set; }

    public Guid ActorId { get; set; }

    public string Field { get; set; } = string.Empty;

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }
}
