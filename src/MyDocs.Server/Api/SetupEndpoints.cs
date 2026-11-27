using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure;
using MyDocs.Server.Infrastructure.Auth;
using MyDocs.Shared.Auth;
using MyDocs.Shared.Household;

namespace MyDocs.Server.Api;

/// <summary>
/// Einrichtungsassistent: erstes Konto als Administrator, mit Einrichtungscode aus dem Protokoll und Pflicht zum
/// zweiten Faktor. Nur erreichbar, solange myDocs nicht eingerichtet ist.
/// </summary>
internal static class SetupEndpoints
{
    public const string RateLimitPolicy = "setup";

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

        return TypedResults.Ok(await TwoFactorEnrollment.StartAsync(users, user));
    }

    private static async Task<IResult> FinishAsync(SetupFinishRequest request, MyDocsDbContext db, UserManager<AppUser> users,
        SignInManager<AppUser> signIn, RoleManager<IdentityRole<Guid>> roles, SetupTokenService tokens, Infrastructure.Audit.AuditLog audit, TimeProvider time, CancellationToken ct)
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

        if (await TwoFactorEnrollment.FinishAsync(users, user, request.Code) is not { } codes)
        {
            return ApiProblems.Of(AuthErrors.CodeInvalid);
        }

        // Der erste Benutzer ist Administrator und Mitglied des Haushalts, und er ist eine Person (77).
        await HouseholdAccounts.EnsureRolesAsync(roles);
        await users.AddToRolesAsync(user, [Roles.Admin, HouseholdRoles.Member]);
        db.Persons.Add(new Person { Id = Guid.CreateVersion7(), DisplayName = user.DisplayName, UserId = user.Id, CreatedAt = time.GetUtcNow() });
        audit.Add(MyDocs.Shared.Audit.AuditActions.SetupCompleted, user.Id, title: user.DisplayName);
        await db.SaveChangesAsync(ct);
        tokens.Revoke();
        await signIn.SignInAsync(user, isPersistent: false);
        return TypedResults.Ok(codes);
    }
}
