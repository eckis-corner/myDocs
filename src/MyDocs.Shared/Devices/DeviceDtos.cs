namespace MyDocs.Shared.Devices;

/// <summary>Freigabe im Browser (41, 152): Der Desktop-Client wartet auf <see cref="RedirectUri"/> (Loopback) auf den Code.</summary>
public sealed record DeviceAuthorizeRequest(string RedirectUri, string CodeChallenge, string CodeChallengeMethod, string DeviceName, string? State);

/// <summary>Adresse, zu der der Browser weiterleitet: Loopback des Clients mit Code und State.</summary>
public sealed record DeviceAuthorizeResult(string RedirectUrl);

/// <summary>Tausch von Code (mit Verifier) oder Refresh-Token gegen neue Tokens.</summary>
public sealed record DeviceTokenRequest(string GrantType, string? Code = null, string? CodeVerifier = null, string? RedirectUri = null, string? RefreshToken = null);

public sealed record DeviceTokenDto(string AccessToken, int ExpiresIn, string RefreshToken, Guid DeviceId);

/// <summary>Ein angemeldetes Gerät; <see cref="Current"/> ist das, von dem die Anfrage kommt.</summary>
public sealed record DeviceDto(Guid Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset LastUsedAt, string? LastIp, bool Current);

public static class DeviceGrants
{
    public const string AuthorizationCode = "authorization_code";
    public const string RefreshToken = "refresh_token";
}

/// <summary>Fehlercodes; Texte unter <c>DeviceError_{Code}</c>.</summary>
public static class DeviceErrors
{
    public const string InvalidRequest = "invalid-request";
    public const string InvalidRedirect = "invalid-redirect";
    public const string InvalidGrant = "invalid-grant";
    public const string NotFound = "device-not-found";
}

/// <summary>Prüfungen, gleich in Client und Server.</summary>
public static class DeviceRules
{
    public const int MaxNameLength = 100;

    /// <summary>Nur Loopback über http mit Port (RFC 8252, 7.3), ohne Query und Fragment.</summary>
    public static bool IsLoopbackRedirect(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttp && !uri.IsDefaultPort
        && uri.Host is "127.0.0.1" or "[::1]" or "localhost" && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
        && string.IsNullOrEmpty(uri.UserInfo);

    /// <summary>base64url ohne Auffüllung, 43–128 Zeichen (RFC 7636).</summary>
    public static bool IsBase64Url(string? value, int min, int max) =>
        value is not null && value.Length >= min && value.Length <= max && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '~');
}
