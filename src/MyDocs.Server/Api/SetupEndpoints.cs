using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure;
using MyDocs.Server.Infrastructure.Auth;
using MyDocs.Shared.Auth;

namespace MyDocs.Server.Api;

/// <summary>
/// Einrichtungsassistent: erstes Konto als Administrator, mit Einrichtungscode aus dem Protokoll und Pflicht zum
/// zweiten Faktor. Nur erreichbar, solange myDocs nicht eingerichtet ist.
/// </summary>
internal static class SetupEndpoints
{
    public const string RateLimitPolicy = "setup";
    private const string Issuer = "myDocs";

    public static IEndpointRouteBuilder MapSetupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/setup").AllowAnonymous().RequireRateLimiting(RateLimitPolicy);
        group.MapGet("/status", async (MyDocsDbContext db, CancellationToken ct) => TypedResults.Ok(new SetupStatusDto(await IsRequiredAsync(db, ct))));
        group.MapPost("/start", StartAsync);
        group.MapPost("/finish", FinishAsync);
        return app;
    }

    private static Task<bool> IsRequiredAsync(MyDocsDbContext db, CancellationToken ct) => db.Users.AllAsync(u => !u.TwoFactorEnabled, ct);

    private static async Task<IResult> StartAsync(SetupStartRequest request, MyDocsDbContext db, UserManager<AppUser> users,
        SetupTokenService tokens, TimeProvider time, CancellationToken ct)
    {
        if (!await IsRequiredAsync(db, ct))
        {
            return ApiProblems.Of(AuthErrors.SetupDone, StatusCodes.Status409Conflict);
        }

        if (!tokens.Verify(request.SetupCode))
        {
            return ApiProblems.Of(AuthErrors.SetupCodeInvalid, StatusCodes.Status403Forbidden);
        }

        if (!MailAddress.TryCreate(request.Email?.Trim(), out var email))
        {
            return ApiProblems.Of(AuthErrors.EmailInvalid);
        }

        // Ein abgebrochener Versuch hinterlässt ein Konto ohne zweiten Faktor; es wird ersetzt.
        foreach (var abandoned in await db.Users.Where(u => !u.TwoFactorEnabled).ToListAsync(ct))
        {
            await users.DeleteAsync(abandoned);
        }

        var user = new AppUser
        {
            Email = email.Address,
            UserName = email.Address,
            EmailConfirmed = true,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? email.Address : request.DisplayName.Trim(),
            CreatedAt = time.GetUtcNow(),
        };
        var created = await users.CreateAsync(user, request.Password ?? string.Empty);
        if (!created.Succeeded)
        {
            return ApiProblems.Of(created.Errors.Any(e => e.Code.StartsWith("Password", StringComparison.Ordinal)) ? AuthErrors.PasswordWeak : AuthErrors.EmailInvalid);
        }

        await users.ResetAuthenticatorKeyAsync(user);
        var secret = (await users.GetAuthenticatorKeyAsync(user))!;
        return TypedResults.Ok(new TotpSetupDto(secret, OtpAuthUri(email.Address, secret)));
    }

    private static async Task<IResult> FinishAsync(SetupFinishRequest request, MyDocsDbContext db, UserManager<AppUser> users,
        SignInManager<AppUser> signIn, RoleManager<IdentityRole<Guid>> roles, SetupTokenService tokens, CancellationToken ct)
    {
        if (!await IsRequiredAsync(db, ct))
        {
            return ApiProblems.Of(AuthErrors.SetupDone, StatusCodes.Status409Conflict);
        }

        if (!tokens.Verify(request.SetupCode))
        {
            return ApiProblems.Of(AuthErrors.SetupCodeInvalid, StatusCodes.Status403Forbidden);
        }

        var user = await users.FindByEmailAsync(request.Email?.Trim() ?? string.Empty);
        if (user is null || user.TwoFactorEnabled)
        {
            return ApiProblems.Of(AuthErrors.Invalid, StatusCodes.Status404NotFound);
        }

        if (!await users.VerifyTwoFactorTokenAsync(user, OneTimeTotpProvider.Name, request.Code ?? string.Empty))
        {
            return ApiProblems.Of(AuthErrors.CodeInvalid);
        }

        if (!await roles.RoleExistsAsync(Roles.Admin))
        {
            await roles.CreateAsync(new IdentityRole<Guid>(Roles.Admin));
        }

        await users.AddToRoleAsync(user, Roles.Admin);
        await users.SetTwoFactorEnabledAsync(user, true);
        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10) ?? [];
        tokens.Revoke();
        await signIn.SignInAsync(user, isPersistent: false);
        return TypedResults.Ok(new RecoveryCodesDto([.. codes]));
    }

    /// <summary>otpauth-URI für Authenticator-Apps (RFC 6238, SHA-1, 6 Ziffern, 30 Sekunden).</summary>
    internal static string OtpAuthUri(string account, string secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(account)}" +
        $"?secret={secret}&issuer={Uri.EscapeDataString(Issuer)}&algorithm=SHA1&digits=6&period=30";
}
