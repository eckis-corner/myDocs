namespace MyDocs.Server.Data;

/// <summary>Einmal-Token, etwa für den Aktivierungslink. Die Datenbank kennt nur den SHA-256-Hash.</summary>
public sealed class OneTimeToken
{
    public Guid Id { get; set; }

    public string Purpose { get; set; } = string.Empty;

    public Guid UserId { get; set; }

    public byte[] TokenHash { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? UsedAt { get; set; }
}

public static class TokenPurposes
{
    public const string Activation = "activation";
}
