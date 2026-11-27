using MyCommon.Catalog;

namespace MyDocs.Shared.Objects;

/// <summary>
/// Inhalt eines Objekts: Titel, Felder des Katalogs in kanonischer Form (<see cref="FieldValues"/>), gewählte
/// Ablageorte, Notizen und Ablaufdatum. Gemeinsam für Formular, API und den späteren Abgleich mit myLife (56).
/// </summary>
public sealed class ObjectData : IEditableFieldValues
{
    public string Title { get; set; } = string.Empty;

    public Dictionary<string, string> Fields { get; set; } = [];

    /// <summary>Ablageort-Felder: Feldschlüssel → ID des Ablageorts; der Name steht zusätzlich im Feld (wie myLife, 143).</summary>
    public Dictionary<string, Guid> Locations { get; set; } = [];

    public string? Notes { get; set; }

    /// <summary>Ablaufdatum, Bedeutung je Typ (Vertragsende, Gültigkeit …).</summary>
    public DateOnly? ExpiresOn { get; set; }

    public string Get(string key) => Fields.TryGetValue(key, out var value) ? value : string.Empty;

    public void Set(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Fields.Remove(key);
        }
        else
        {
            Fields[key] = value.Trim();
        }
    }

    public void SetLocation(string key, Guid? id, string? name)
    {
        if (id is { } location && !string.IsNullOrWhiteSpace(name))
        {
            Locations[key] = location;
            Set(key, name);
        }
        else
        {
            Locations.Remove(key);
            Set(key, null);
        }
    }

    public ObjectData Clone() => new()
    {
        Title = Title,
        Fields = new(Fields),
        Locations = new(Locations),
        Notes = Notes,
        ExpiresOn = ExpiresOn,
    };
}

/// <summary>Eintrag in Listen: genug für Tabelle, Suche und Verweise.</summary>
public sealed record ObjectSummaryDto(Guid Id, string TypeKey, string Title, Guid? OwnerPersonId, Guid? ResponsiblePersonId,
    DateOnly? ExpiresOn, DateTimeOffset UpdatedAt, Dictionary<string, string> Fields, string? LifecycleState = null, DateOnly? LifecycleDate = null);

/// <summary>Ein Objekt mit allem, was die Objektseite braucht, und den Rechten des Aufrufers.</summary>
public sealed record ObjectDto(Guid Id, string TypeKey, ObjectData Data, Guid? OwnerPersonId, Guid? ResponsiblePersonId,
    IReadOnlyList<ObjectShareDto> Shares, bool CanEdit, bool CanManage, uint Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt, string? LifecycleState = null, DateOnly? LifecycleDate = null, Guid? ReplacedById = null);

/// <summary>Neu anlegen oder speichern. <paramref name="OwnerPersonId"/> null heißt Haushalt.</summary>
public sealed record ObjectSaveRequest(string TypeKey, ObjectData Data, Guid? OwnerPersonId, Guid? ResponsiblePersonId, uint Version = 0);

/// <summary>Freigabe eines privaten Objekts an ein Konto (33).</summary>
public sealed record ObjectShareDto(Guid UserId, bool CanEdit);

/// <summary>Eine Änderung der Historie (27): ein Feld von alt auf neu, wer, wann.</summary>
public sealed record ObjectHistoryDto(DateTimeOffset At, string ActorName, string Field, string? OldValue, string? NewValue);

/// <summary>Fehlercodes rund um Objekte; Texte unter <c>ObjectError_{Code}</c>, Feldfehler unter <c>FieldError_</c>.</summary>
public static class ObjectErrors
{
    public const string TypeUnknown = "type-unknown";
    public const string Invalid = "invalid";
    public const string NotFound = "not-found";
    public const string Forbidden = "forbidden";
    public const string Conflict = "conflict";
    public const string ResponsibleRequired = "responsible-required";
    public const string OwnerInvalid = "owner-invalid";
    public const string TypeOnlyInMyLife = "type-only-in-mylife";
    public const string LifecycleInvalid = "lifecycle-invalid";
    public const string LifecycleDateRequired = "lifecycle-date-required";
    public const string ReplacementInvalid = "replacement-invalid";
}

/// <summary>Validierungsfehler eines Objekts: Feldschlüssel (oder <c>title</c>) → Fehlercode.</summary>
public sealed record ObjectValidationProblem(string Title, Dictionary<string, string> Errors);
