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
