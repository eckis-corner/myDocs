using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Audit;
using MyDocs.Server.Infrastructure.Auth;
using MyDocs.Shared.Audit;
using MyDocs.Shared.Auth;

namespace MyDocs.Server.Api;

/// <summary>Anmeldung mit Passwort und zweitem Faktor (40), Abmelden, eigenes Konto.</summary>
internal static partial class AuthEndpoints
{
    public const string RateLimitPolicy = "auth";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");
        group.MapPost("/login", LoginAsync).AllowAnonymous().RequireRateLimiting(RateLimitPolicy);
        group.MapPost("/login/2fa", TwoFactorAsync).AllowAnonymous().RequireRateLimiting(RateLimitPolicy);
        group.MapPost("/login/passkey/options", PasskeyOptionsAsync).AllowAnonymous().RequireRateLimiting(RateLimitPolicy);
        group.MapPost("/login/passkey", PasskeyAsync).AllowAnonymous().RequireRateLimiting(RateLimitPolicy);
        group.MapPost("/logout", async (SignInManager<AppUser> signIn, ClaimsPrincipal principal, AuditLog audit) =>
        {
            if (principal.Identity?.IsAuthenticated == true)
            {
                await audit.WriteAsync(AuditActions.Logout);
            }

            await signIn.SignOutAsync();
            return TypedResults.NoContent();
        }).AllowAnonymous();
        group.MapGet("/me", MeAsync).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> LoginAsync(LoginRequest request, UserManager<AppUser> users, SignInManager<AppUser> signIn, AuditLog audit)
    {
        var user = await users.FindByEmailAsync(request.Email?.Trim() ?? string.Empty);
        if (user is null)
        {
            // Gleiche Antwortzeit wie bei bekannten Adressen: Hash eines Scheinpassworts.
            users.PasswordHasher.HashPassword(new AppUser(), request.Password ?? string.Empty);
            return ApiProblems.Of(AuthErrors.Invalid, StatusCodes.Status401Unauthorized);
        }

        if (HouseholdAccounts.IsLocked(user))
        {
            // Vom Administrator gesperrt; gleiche Rechenzeit wie eine echte Prüfung.
            users.PasswordHasher.VerifyHashedPassword(user, user.PasswordHash ?? string.Empty, request.Password ?? string.Empty);
            return ApiProblems.Of(AuthErrors.Disabled, StatusCodes.Status403Forbidden);
        }

        var result = await signIn.PasswordSignInAsync(user, request.Password ?? string.Empty, isPersistent: false, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            await audit.WriteAsync(AuditActions.LoginFailed, user.Id, details: "gesperrt nach Fehlversuchen");
            return ApiProblems.Of(AuthErrors.Locked, StatusCodes.Status429TooManyRequests);
        }

        if (result.RequiresTwoFactor)
        {
            return TypedResults.Ok(new LoginResultDto(TwoFactorRequired: true, PasskeyAvailable: (await users.GetPasskeysAsync(user)).Count > 0));
        }

        if (result.Succeeded)
        {
            // Ohne zweiten Faktor gibt es keine Anmeldung (nur ein abgebrochener Einrichtungsversuch kommt hierher).
            await signIn.SignOutAsync();
            return ApiProblems.Of(AuthErrors.TwoFactorMissing, StatusCodes.Status403Forbidden);
        }

        await audit.WriteAsync(AuditActions.LoginFailed, user.Id, details: "Passwort");
        return ApiProblems.Of(AuthErrors.Invalid, StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> TwoFactorAsync(LoginTwoFactorRequest request, SignInManager<AppUser> signIn, UserManager<AppUser> users, AuditLog audit)
    {
        var pending = await signIn.GetTwoFactorAuthenticationUserAsync();
        var code = (request.Code ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal);
        var result = request.IsRecoveryCode
            ? await signIn.TwoFactorRecoveryCodeSignInAsync(code)
            : await signIn.TwoFactorSignInAsync(OneTimeTotpProvider.Name, code, isPersistent: false, rememberClient: false);
        if (result.IsLockedOut)
        {
            return ApiProblems.Of(AuthErrors.Locked, StatusCodes.Status429TooManyRequests);
        }

        if (!result.Succeeded)
        {
            if (pending is not null)
            {
                await audit.WriteAsync(AuditActions.LoginFailed, pending.Id, details: request.IsRecoveryCode ? "Wiederherstellungscode" : "Code");
            }

            return ApiProblems.Of(AuthErrors.CodeInvalid, StatusCodes.Status401Unauthorized);
        }

        await audit.WriteAsync(AuditActions.Login, pending?.Id, details: request.IsRecoveryCode ? "Wiederherstellungscode" : null);
        return TypedResults.NoContent();
    }

    private const string PasskeyStepCookie = "mydocs.passkey-step";

    /// <summary>
    /// Zweiter Faktor per Passkey: nur nach dem Passwort und nur für die Passkeys dieses Kontos. Identity legt den
    /// Zustand der Zeremonie im Zwischen-Cookie ab und vergisst dabei, wer das Passwort schon eingegeben hat; das hält
    /// ein eigenes, signiertes Cookie fest (5 Minuten).
    /// </summary>
    private static async Task<IResult> PasskeyOptionsAsync(SignInManager<AppUser> signIn, IDataProtectionProvider protection, HttpContext context)
    {
        if (await signIn.GetTwoFactorAuthenticationUserAsync() is not { } pending)
        {
            return ApiProblems.Of(AuthErrors.Invalid, StatusCodes.Status401Unauthorized);
        }

        var options = await signIn.MakePasskeyRequestOptionsAsync(pending);
        context.Response.Cookies.Append(PasskeyStepCookie, StepProtector(protection).Protect(pending.Id.ToString(), TimeSpan.FromMinutes(5)), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = context.Request.IsHttps,
            MaxAge = TimeSpan.FromMinutes(5),
            Path = "/api/auth",
        });
        return TypedResults.Ok(new PasskeyOptionsDto(options));
    }

    private static ITimeLimitedDataProtector StepProtector(IDataProtectionProvider protection) =>
        protection.CreateProtector("myDocs.PasskeyStep.v1").ToTimeLimitedDataProtector();

    /// <summary>Konto, das das Passwort eingegeben und danach Passkey-Optionen geholt hat; null ohne gültiges Cookie.</summary>
    private static async Task<AppUser?> PasskeyStepUserAsync(HttpContext context, IDataProtectionProvider protection, UserManager<AppUser> users)
    {
        if (context.Request.Cookies[PasskeyStepCookie] is not { Length: > 0 } value)
        {
            return null;
        }

        try
        {
            return await users.FindByIdAsync(StepProtector(protection).Unprotect(value));
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    private static async Task<IResult> PasskeyAsync(PasskeyCredentialRequest request, SignInManager<AppUser> signIn, UserManager<AppUser> users, AuditLog audit,
        IDataProtectionProvider protection, HttpContext context, ILoggerFactory loggers)
    {
        if (await PasskeyStepUserAsync(context, protection, users) is not { } pending)
        {
            return ApiProblems.Of(AuthErrors.Invalid, StatusCodes.Status401Unauthorized);
        }

        context.Response.Cookies.Delete(PasskeyStepCookie, new CookieOptions { Path = "/api/auth" });

        if (await users.IsLockedOutAsync(pending))
        {
            return ApiProblems.Of(AuthErrors.Locked, StatusCodes.Status429TooManyRequests);
        }

        var result = await signIn.PerformPasskeyAssertionAsync(request.CredentialJson ?? string.Empty);
        if (!result.Succeeded || result.User.Id != pending.Id)
        {
            LogPasskeyRejected(loggers.CreateLogger(typeof(AuthEndpoints)), result.Failure?.Message ?? "anderes Konto");
            await users.AccessFailedAsync(pending);
            await audit.WriteAsync(AuditActions.LoginFailed, pending.Id, details: "Passkey");
            return ApiProblems.Of(AuthErrors.PasskeyInvalid, StatusCodes.Status401Unauthorized);
        }

        await users.AddOrUpdatePasskeyAsync(pending, result.Passkey);
        await users.ResetAccessFailedCountAsync(pending);
        await signIn.Context.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
        await signIn.SignInWithClaimsAsync(pending, isPersistent: false, [new Claim("amr", "passkey")]);
        await audit.WriteAsync(AuditActions.Login, pending.Id, details: "Passkey");
        return TypedResults.NoContent();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Anmeldung mit Passkey abgelehnt: {Reason}")]
    private static partial void LogPasskeyRejected(ILogger logger, string reason);

    private static async Task<IResult> MeAsync(ClaimsPrincipal principal, UserManager<AppUser> users, MyDocsDbContext db, CancellationToken ct)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(new MeDto(user.Id, user.Email ?? string.Empty, user.DisplayName, await users.IsInRoleAsync(user, Roles.Admin),
            await HouseholdAccounts.HouseholdRoleAsync(users, user), await HouseholdAccounts.PersonIdAsync(db, user.Id, ct),
            await users.CountRecoveryCodesAsync(user)));
    }
}
