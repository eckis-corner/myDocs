namespace MyDocs.Shared.Admin;

/// <summary>Zustand eines Kontos: wartet auf Aktivierung, aktiv oder gesperrt.</summary>
public static class UserStates
{
    public const string Pending = "pending";
    public const string Active = "active";
    public const string Locked = "locked";
}

public sealed record AdminUserDto(Guid Id, string Email, string DisplayName, string Role, bool IsAdmin, string State, DateTimeOffset CreatedAt, DateTimeOffset? ActivationExpiresAt);

/// <summary>Neues Konto: Der Administrator gibt den Aktivierungslink selbst weiter (bis der Mailversand kommt).</summary>
public sealed record CreateUserRequest(string Email, string DisplayName, string Role, bool IsAdmin);

public sealed record UpdateUserRequest(string DisplayName, string Role, bool IsAdmin);

/// <summary>Einmal-Token für den Aktivierungslink <c>/activate#{Token}</c>; gültig bis <paramref name="ExpiresAt"/>.</summary>
public sealed record ActivationLinkDto(Guid UserId, string Token, DateTimeOffset ExpiresAt);

/// <summary>Eine Sicherung im Ordner <c>backup</c>.</summary>
public sealed record BackupItemDto(string Name, DateTimeOffset CreatedAt, long Size);

/// <summary>Stand der Sicherung: Schlüssel eingerichtet, letzte Sicherung, letzter Fehler, nächster Lauf, Generationen.</summary>
public sealed record BackupStatusDto(bool Configured, DateTimeOffset? LastSuccessAt, string? LastError, DateTimeOffset? LastErrorAt,
    DateTimeOffset? NextRunAt, IReadOnlyList<BackupItemDto> Items);

/// <summary>Hauptschlüssel anzeigen (46): nur mit einem frischen Code der eigenen Authenticator-App.</summary>
public sealed record MasterKeyRequest(string Code);

/// <summary>Der Hauptschlüssel als 24 Wörter (BIP-39) mit dem Zeitpunkt der Anzeige.</summary>
public sealed record MasterKeyDto(IReadOnlyList<string> Words, DateTimeOffset ShownAt);
