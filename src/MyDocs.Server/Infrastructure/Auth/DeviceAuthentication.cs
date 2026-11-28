using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyDocs.Server.Data;

namespace MyDocs.Server.Infrastructure.Auth;

/// <summary>
/// Zugriffstoken der Geräte (41, 152): 15 Minuten gültig, mit Data Protection geschützt und mit Gerät, Konto und
/// Security Stamp darin. Jede Anfrage prüft, ob das Gerät noch gilt und das Konto unverändert ist; Sperre,
/// Zurücksetzen oder neue Rechte beenden damit auch die Geräte.
/// </summary>
public sealed class DeviceTokens(IDataProtectionProvider protection, TimeProvider time)
{
    public const string Scheme = "Device";

    /// <summary>Wählt je Anfrage: Bearer → Gerät, sonst das Sitzungs-Cookie.</summary>
    public const string SelectorScheme = "MyDocs";

    public const string DeviceClaim = "mydocs:device";

    public static readonly TimeSpan AccessLifetime = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(90);

    private const string Purpose = "MyDocs.Device.Access.v1";

    public sealed record Access(Guid DeviceId, Guid UserId, string SecurityStamp, DateTimeOffset ExpiresAt);

    public string Issue(Guid deviceId, Guid userId, string stamp)
    {
        var expires = time.GetUtcNow() + AccessLifetime;
        var payload = string.Join('|', "v1", deviceId.ToString("N"), userId.ToString("N"), stamp, expires.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        return protection.CreateProtector(Purpose).Protect(payload);
    }

    public Access? Read(string token)
    {
        try
        {
            var parts = protection.CreateProtector(Purpose).Unprotect(token).Split('|');
            if (parts is not ["v1", var device, var user, var stamp, var expires]
                || !Guid.TryParseExact(device, "N", out var deviceId) || !Guid.TryParseExact(user, "N", out var userId)
                || !long.TryParse(expires, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
            {
                return null;
            }

            var access = new Access(deviceId, userId, stamp, DateTimeOffset.FromUnixTimeSeconds(seconds));
            return access.ExpiresAt > time.GetUtcNow() ? access : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public static string NewSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Hash(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));
}

/// <summary>Anmeldung über <c>Authorization: Bearer</c> mit einem Zugriffstoken aus <see cref="DeviceTokens"/>.</summary>
public sealed class DeviceAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    DeviceTokens tokens, MyDocsDbContext db, UserManager<AppUser> users, IUserClaimsPrincipalFactory<AppUser> principals, TimeProvider time)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }

        if (tokens.Read(header["Bearer ".Length..].Trim()) is not { } access)
        {
            return AuthenticateResult.Fail("Zugriffstoken ungültig oder abgelaufen.");
        }

        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == access.DeviceId && d.UserId == access.UserId && d.RevokedAt == null, Context.RequestAborted);
        var user = device is null ? null : await users.FindByIdAsync(access.UserId.ToString());
        var now = time.GetUtcNow();
        if (device is null || user is null || user.SecurityStamp != access.SecurityStamp || user.LockoutEnd > now)
        {
            return AuthenticateResult.Fail("Gerät widerrufen oder Konto geändert.");
        }

        if (now - device.LastUsedAt > TimeSpan.FromMinutes(5))
        {
            device.LastUsedAt = now;
            device.LastIp = Audit.AuditLog.Truncate(Context.Connection.RemoteIpAddress);
            await db.SaveChangesAsync(Context.RequestAborted);
        }

        var principal = await principals.CreateAsync(user);
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(DeviceTokens.DeviceClaim, device.Id.ToString()));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    // Wie beim Cookie mit Inhalt antworten, sonst macht die Statusseite daraus eine HTML-Seite.
    protected override Task HandleChallengeAsync(AuthenticationProperties properties) => WriteAsync(StatusCodes.Status401Unauthorized, "auth-required");

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) => WriteAsync(StatusCodes.Status403Forbidden, "auth-forbidden");

    private Task WriteAsync(int status, string code)
    {
        Response.StatusCode = status;
        if (status == StatusCodes.Status401Unauthorized)
        {
            Response.Headers.WWWAuthenticate = "Bearer";
        }

        return Response.WriteAsJsonAsync(new { title = code, status });
    }
}
