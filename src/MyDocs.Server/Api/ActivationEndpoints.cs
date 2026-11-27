using Microsoft.AspNetCore.Identity;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Auth;
using MyDocs.Shared.Auth;

namespace MyDocs.Server.Api;

/// <summary>
/// Aktivierung über den Link des Administrators: Passwort festlegen, Authenticator-App einrichten,
/// Wiederherstellungscodes. Der Link gilt einmal und sieben Tage.
/// </summary>
internal static class ActivationEndpoints
{
    public static IEndpointRouteBuilder MapActivationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/activation").AllowAnonymous().RequireRateLimiting(AuthEndpoints.RateLimitPolicy);
        group.MapPost("/info", InfoAsync);
        group.MapPost("/start", StartAsync);
        group.MapPost("/finish", FinishAsync);
        return app;
    }

    private static async Task<AppUser?> PendingUserAsync(string? token, OneTimeTokens tokens, UserManager<AppUser> users, CancellationToken ct)
    {
        if (await tokens.FindAsync(token, TokenPurposes.Activation, ct) is not { } entry)
        {
            return null;
        }

        var user = await users.FindByIdAsync(entry.UserId.ToString());
        return user is { TwoFactorEnabled: false } && !HouseholdAccounts.IsLocked(user) ? user : null;
    }

    private static async Task<IResult> InfoAsync(ActivationTokenRequest request, OneTimeTokens tokens, UserManager<AppUser> users, CancellationToken ct) =>
        await PendingUserAsync(request.Token, tokens, users, ct) is { } user
            ? TypedResults.Ok(new ActivationInfoDto(user.Email ?? string.Empty, user.DisplayName))
            : ApiProblems.Of(AuthErrors.ActivationInvalid, StatusCodes.Status404NotFound);

    private static async Task<IResult> StartAsync(ActivationStartRequest request, OneTimeTokens tokens, UserManager<AppUser> users, CancellationToken ct)
    {
        if (await PendingUserAsync(request.Token, tokens, users, ct) is not { } user)
        {
            return ApiProblems.Of(AuthErrors.ActivationInvalid, StatusCodes.Status404NotFound);
        }

        // Ein zweiter Versuch über denselben Link ersetzt das Passwort des ersten.
        if (await users.HasPasswordAsync(user))
        {
            await users.RemovePasswordAsync(user);
        }

        var added = await users.AddPasswordAsync(user, request.Password ?? string.Empty);
        if (!added.Succeeded)
        {
            return ApiProblems.Of(AuthErrors.PasswordWeak);
        }

        return TypedResults.Ok(await TwoFactorEnrollment.StartAsync(users, user));
    }

    private static async Task<IResult> FinishAsync(ActivationFinishRequest request, OneTimeTokens tokens, UserManager<AppUser> users,
        SignInManager<AppUser> signIn, Infrastructure.Audit.AuditLog audit, CancellationToken ct)
    {
        if (await tokens.FindAsync(request.Token, TokenPurposes.Activation, ct) is not { } entry
            || await users.FindByIdAsync(entry.UserId.ToString()) is not { TwoFactorEnabled: false } user
            || HouseholdAccounts.IsLocked(user) || !await users.HasPasswordAsync(user))
        {
            return ApiProblems.Of(AuthErrors.ActivationInvalid, StatusCodes.Status404NotFound);
        }

        if (await TwoFactorEnrollment.FinishAsync(users, user, request.Code) is not { } codes)
        {
            return ApiProblems.Of(AuthErrors.CodeInvalid);
        }

        audit.Add(MyDocs.Shared.Audit.AuditActions.Activated, user.Id, title: user.DisplayName);
        await tokens.ConsumeAsync(entry, ct);
        await signIn.SignInAsync(user, isPersistent: false);
        return TypedResults.Ok(codes);
    }
}
