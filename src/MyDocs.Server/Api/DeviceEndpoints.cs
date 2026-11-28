using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Audit;
using MyDocs.Server.Infrastructure.Auth;
using MyDocs.Shared.Audit;
using MyDocs.Shared.Devices;

namespace MyDocs.Server.Api;

/// <summary>
/// Anmeldung des Desktop-Clients (41, 152): Authorization Code mit PKCE über den Systembrowser und eine
/// Loopback-Adresse, danach Zugriffs- und Refresh-Token je Gerät. Kein eigener Identity-Server: Die Freigabe gibt die
/// angemeldete Person in der Web-App, mit Passwort und zweitem Faktor wie immer.
/// </summary>
internal static class DeviceEndpoints
{
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(2);

    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/devices");
        group.MapPost("/authorize", AuthorizeAsync).RequireAuthorization();
        group.MapPost("/token", TokenAsync).AllowAnonymous().RequireRateLimiting(AuthEndpoints.RateLimitPolicy);
        group.MapGet("", ListAsync).RequireAuthorization();
        group.MapDelete("/{id:guid}", RevokeAsync).RequireAuthorization();
        return app;
    }

    /// <summary>Freigabe im Browser; nur mit dem Sitzungs-Cookie, ein Gerät legt kein weiteres Gerät an.</summary>
    private static async Task<IResult> AuthorizeAsync(DeviceAuthorizeRequest request, ClaimsPrincipal principal, MyDocsDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (principal.HasClaim(c => c.Type == DeviceTokens.DeviceClaim))
        {
            return TypedResults.Forbid();
        }

        if (!DeviceRules.IsLoopbackRedirect(request.RedirectUri))
        {
            return ApiProblems.Of(DeviceErrors.InvalidRedirect);
        }

        var name = Clean(request.DeviceName);
        if (request.CodeChallengeMethod != "S256" || !DeviceRules.IsBase64Url(request.CodeChallenge, 43, 128) || name.Length == 0
            || (request.State is { } state && !DeviceRules.IsBase64Url(state, 1, 200)))
        {
            return ApiProblems.Of(DeviceErrors.InvalidRequest);
        }

        var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var now = time.GetUtcNow();
        await db.DeviceAuthorizations.Where(a => a.ExpiresAt < now).ExecuteDeleteAsync(ct);
        var code = DeviceTokens.NewSecret();
        db.DeviceAuthorizations.Add(new DeviceAuthorization
        {
            CodeHash = DeviceTokens.Hash(code),
            UserId = userId,
            DeviceName = name,
            RedirectUri = request.RedirectUri,
            CodeChallenge = request.CodeChallenge,
            ExpiresAt = now + CodeLifetime,
        });
        await db.SaveChangesAsync(ct);
        var query = $"?code={Uri.EscapeDataString(code)}{(request.State is { } s ? $"&state={Uri.EscapeDataString(s)}" : string.Empty)}";
        return TypedResults.Ok(new DeviceAuthorizeResult(request.RedirectUri + query));
    }

    private static async Task<IResult> TokenAsync(DeviceTokenRequest request, MyDocsDbContext db, UserManager<AppUser> users, DeviceTokens tokens,
        AuditLog audit, HttpContext context, TimeProvider time, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return request.GrantType switch
        {
            DeviceGrants.AuthorizationCode => await ExchangeCodeAsync(request, db, users, tokens, audit, context, now, ct),
            DeviceGrants.RefreshToken => await RefreshAsync(request, db, users, tokens, context, now, ct),
            _ => ApiProblems.Of(DeviceErrors.InvalidRequest),
        };
    }

    private static async Task<IResult> ExchangeCodeAsync(DeviceTokenRequest request, MyDocsDbContext db, UserManager<AppUser> users, DeviceTokens tokens,
        AuditLog audit, HttpContext context, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 100 || !DeviceRules.IsBase64Url(request.CodeVerifier, 43, 128))
        {
            return ApiProblems.Of(DeviceErrors.InvalidGrant);
        }

        var hash = DeviceTokens.Hash(request.Code);
        var grant = await db.DeviceAuthorizations.FirstOrDefaultAsync(a => a.CodeHash == hash, ct);
        if (grant is null)
        {
            return ApiProblems.Of(DeviceErrors.InvalidGrant);
        }

        // Ein Code gilt einmal, auch wenn der Tausch scheitert.
        db.DeviceAuthorizations.Remove(grant);
        await db.SaveChangesAsync(ct);
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(request.CodeVerifier!)));
        if (grant.ExpiresAt < now || grant.RedirectUri != request.RedirectUri
            || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(challenge), Encoding.ASCII.GetBytes(grant.CodeChallenge)))
        {
            return ApiProblems.Of(DeviceErrors.InvalidGrant);
        }

        if (await users.FindByIdAsync(grant.UserId.ToString()) is not { } user || user.LockoutEnd > now)
        {
            return ApiProblems.Of(DeviceErrors.InvalidGrant);
        }

        var refresh = DeviceTokens.NewSecret();
        var device = new Device
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            Name = grant.DeviceName,
            RefreshTokenHash = DeviceTokens.Hash(refresh),
            RefreshExpiresAt = now + DeviceTokens.RefreshLifetime,
            SecurityStamp = user.SecurityStamp ?? string.Empty,
            CreatedAt = now,
            LastUsedAt = now,
            LastIp = AuditLog.Truncate(context.Connection.RemoteIpAddress),
        };
        db.Devices.Add(device);
        audit.Add(AuditActions.DeviceAdded, user.Id, title: device.Name);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new DeviceTokenDto(tokens.Issue(device.Id, user.Id, device.SecurityStamp), (int)DeviceTokens.AccessLifetime.TotalSeconds, refresh, device.Id));
    }

    /// <summary>Neues Paar; das alte Refresh-Token gilt danach nicht mehr. Hat sich das Konto geändert, ist das Gerät widerrufen.</summary>
    private static async Task<IResult> RefreshAsync(DeviceTokenRequest request, MyDocsDbContext db, UserManager<AppUser> users, DeviceTokens tokens,
        HttpContext context, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken) || request.RefreshToken.Length > 100)
        {
            return ApiProblems.Of(DeviceErrors.InvalidGrant);
        }

        var hash = DeviceTokens.Hash(request.RefreshToken);
        var device = await db.Devices.FirstOrDefaultAsync(d => d.RefreshTokenHash == hash && d.RevokedAt == null, ct);
        if (device is null || device.RefreshExpiresAt < now)
        {
            return ApiProblems.Of(DeviceErrors.InvalidGrant);
        }

        var user = await users.FindByIdAsync(device.UserId.ToString());
        if (user is null || user.SecurityStamp != device.SecurityStamp || user.LockoutEnd > now)
        {
            device.RevokedAt = now;
            await db.SaveChangesAsync(ct);
            return ApiProblems.Of(DeviceErrors.InvalidGrant);
        }

        var refresh = DeviceTokens.NewSecret();
        device.RefreshTokenHash = DeviceTokens.Hash(refresh);
        device.RefreshExpiresAt = now + DeviceTokens.RefreshLifetime;
        device.LastUsedAt = now;
        device.LastIp = AuditLog.Truncate(context.Connection.RemoteIpAddress);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new DeviceTokenDto(tokens.Issue(device.Id, user.Id, device.SecurityStamp), (int)DeviceTokens.AccessLifetime.TotalSeconds, refresh, device.Id));
    }

    private static async Task<IResult> ListAsync(ClaimsPrincipal principal, MyDocsDbContext db, CancellationToken ct)
    {
        var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var current = principal.FindFirstValue(DeviceTokens.DeviceClaim);
        var devices = await db.Devices.Where(d => d.UserId == userId && d.RevokedAt == null).OrderByDescending(d => d.LastUsedAt).ToListAsync(ct);
        return TypedResults.Ok(devices.Select(d => new DeviceDto(d.Id, d.Name, d.CreatedAt, d.LastUsedAt, d.LastIp, d.Id.ToString() == current)).ToList());
    }

    /// <summary>Widerruft ein eigenes Gerät; sein Zugriff endet sofort, nicht erst mit dem Ablauf des Tokens.</summary>
    private static async Task<IResult> RevokeAsync(Guid id, ClaimsPrincipal principal, MyDocsDbContext db, AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (await db.Devices.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId && d.RevokedAt == null, ct) is not { } device)
        {
            return ApiProblems.Of(DeviceErrors.NotFound, StatusCodes.Status404NotFound);
        }

        device.RevokedAt = time.GetUtcNow();
        audit.Add(AuditActions.DeviceRevoked, userId, title: device.Name);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static string Clean(string? name)
    {
        var text = new string([.. (name ?? string.Empty).Where(c => !char.IsControl(c))]).Trim();
        return text.Length > DeviceRules.MaxNameLength ? text[..DeviceRules.MaxNameLength] : text;
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
