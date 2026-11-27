using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Audit;
using MyDocs.Server.Infrastructure.Auth;
using MyDocs.Server.Infrastructure.Backup;
using MyDocs.Server.Infrastructure.Files;
using MyDocs.Shared.Admin;
using MyDocs.Shared.Audit;
using MyDocs.Shared.Auth;

namespace MyDocs.Server.Api;

/// <summary>Sicherung und Hauptschlüssel für den Administrator (45, 46).</summary>
internal static class AdminBackupEndpoints
{
    public static IEndpointRouteBuilder MapAdminBackupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").RequireAuthorization(Roles.Admin);
        group.MapGet("/backup", (BackupService backup) => TypedResults.Ok(backup.Status()));
        group.MapPost("/backup", RunAsync);
        group.MapPost("/master-key", MasterKeyAsync).RequireRateLimiting(AuthEndpoints.RateLimitPolicy);
        return app;
    }

    private static async Task<IResult> RunAsync(ClaimsPrincipal principal, BackupService backup, CancellationToken ct)
    {
        if (!backup.Configured)
        {
            return ApiProblems.Of("backup-not-configured", StatusCodes.Status409Conflict);
        }

        try
        {
            return TypedResults.Ok(await backup.RunAsync(Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!), ct));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or Npgsql.NpgsqlException)
        {
            return ApiProblems.Of("backup-failed", StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>Zeigt den Hauptschlüssel als 24 Wörter, nur mit einem frischen Code der eigenen Authenticator-App; im Protokoll vermerkt.</summary>
    private static async Task<IResult> MasterKeyAsync(MasterKeyRequest request, ClaimsPrincipal principal, UserManager<AppUser> users, MasterKey master,
        AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        if (await users.GetUserAsync(principal) is not { } user
            || !await users.VerifyTwoFactorTokenAsync(user, OneTimeTotpProvider.Name, request.Code ?? string.Empty))
        {
            return ApiProblems.Of(AuthErrors.CodeInvalid, StatusCodes.Status403Forbidden);
        }

        await audit.WriteAsync(AuditActions.MasterKeyShown, user.Id, ct: ct);
        return TypedResults.Ok(new MasterKeyDto(master.ToWords(), time.GetUtcNow()));
    }
}
