namespace MyDocs.Server.Data;

/// <summary>
/// Angemeldetes Gerät (Desktop-Client, 41, 152): Refresh-Token nur als SHA-256, wechselt bei jeder Erneuerung. Ändert
/// sich der Security Stamp des Kontos (Sperre, Zurücksetzen, neue Rechte), gilt das Gerät nicht mehr.
/// </summary>
public sealed class Device
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Name { get; set; } = string.Empty;

    public byte[] RefreshTokenHash { get; set; } = [];

    public DateTimeOffset RefreshExpiresAt { get; set; }

    /// <summary>Security Stamp des Kontos bei der Anmeldung.</summary>
    public string SecurityStamp { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastUsedAt { get; set; }

    /// <summary>Gekürzte IP wie im Protokoll.</summary>
    public string? LastIp { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>Einmaliger Code zwischen Freigabe im Browser und Tausch gegen Tokens (2 Minuten, PKCE).</summary>
public sealed class DeviceAuthorization
{
    public byte[] CodeHash { get; set; } = [];

    public Guid UserId { get; set; }

    public string DeviceName { get; set; } = string.Empty;

    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>S256 der Code-Verifier, base64url.</summary>
    public string CodeChallenge { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }
}
