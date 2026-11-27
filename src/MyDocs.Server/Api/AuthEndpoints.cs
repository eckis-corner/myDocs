using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Auth;
using MyDocs.Shared.Auth;

namespace MyDocs.Server.Api;

/// <summary>Anmeldung mit Passwort und zweitem Faktor (40), Abmelden, eigenes Konto.</summary>
internal static class AuthEndpoints
{
    public const string RateLimitPolicy = "auth";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");
        group.MapPost("/login", LoginAsync).AllowAnonymous().RequireRateLimiting(RateLimitPolicy);
        group.MapPost("/login/2fa", TwoFactorAsync).AllowAnonymous().RequireRateLimiting(RateLimitPolicy);
        group.MapPost("/logout", async (SignInManager<AppUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return TypedResults.NoContent();
        }).AllowAnonymous();
        group.MapGet("/me", MeAsync).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> LoginAsync(LoginRequest request, UserManager<AppUser> users, SignInManager<AppUser> signIn)
    {
        var user = await users.FindByEmailAsync(request.Email?.Trim() ?? string.Empty);
        if (user is null)
        {
            // Gleiche Antwortzeit wie bei bekannten Adressen: Hash eines Scheinpassworts.
            users.PasswordHasher.HashPassword(new AppUser(), request.Password ?? string.Empty);
            return ApiProblems.Of(AuthErrors.Invalid, StatusCodes.Status401Unauthorized);
        }

        var result = await signIn.PasswordSignInAsync(user, request.Password ?? string.Empty, isPersistent: false, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            return ApiProblems.Of(AuthErrors.Locked, StatusCodes.Status429TooManyRequests);
        }

        if (result.RequiresTwoFactor)
        {
            return TypedResults.Ok(new LoginResultDto(TwoFactorRequired: true));
        }

        if (result.Succeeded)
        {
            // Ohne zweiten Faktor gibt es keine Anmeldung (nur ein abgebrochener Einrichtungsversuch kommt hierher).
            await signIn.SignOutAsync();
            return ApiProblems.Of(AuthErrors.TwoFactorMissing, StatusCodes.Status403Forbidden);
        }

        return ApiProblems.Of(AuthErrors.Invalid, StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> TwoFactorAsync(LoginTwoFactorRequest request, SignInManager<AppUser> signIn, UserManager<AppUser> users)
    {
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
            return ApiProblems.Of(AuthErrors.CodeInvalid, StatusCodes.Status401Unauthorized);
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> MeAsync(ClaimsPrincipal principal, UserManager<AppUser> users)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(new MeDto(user.Id, user.Email ?? string.Empty, user.DisplayName, await users.IsInRoleAsync(user, Roles.Admin),
            await users.CountRecoveryCodesAsync(user)));
    }
}
