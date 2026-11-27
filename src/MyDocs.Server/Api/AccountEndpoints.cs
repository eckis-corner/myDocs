using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Audit;
using MyDocs.Shared.Audit;
using MyDocs.Shared.Auth;

namespace MyDocs.Server.Api;

/// <summary>Eigenes Konto: Passkeys verwalten (40, M1.2b). Ein Passkey ersetzt bei der Anmeldung den Code der Authenticator-App.</summary>
internal static partial class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/account/passkeys").RequireAuthorization();
        group.MapGet("/", ListAsync);
        group.MapPost("/options", OptionsAsync);
        group.MapPost("/", AddAsync);
        group.MapDelete("/{id}", RemoveAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(ClaimsPrincipal principal, UserManager<AppUser> users)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var passkeys = await users.GetPasskeysAsync(user);
        return TypedResults.Ok(passkeys.OrderBy(p => p.CreatedAt).Select(p => new PasskeyDto(Base64Url(p.CredentialId), p.Name ?? "Passkey", p.CreatedAt)).ToList());
    }

    private static async Task<IResult> OptionsAsync(ClaimsPrincipal principal, UserManager<AppUser> users, SignInManager<AppUser> signIn)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var json = await signIn.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity
        {
            Id = user.Id.ToString(),
            Name = user.Email ?? user.UserName ?? user.Id.ToString(),
            DisplayName = user.DisplayName,
        });
        return TypedResults.Ok(new PasskeyOptionsDto(json));
    }

    private static async Task<IResult> AddAsync(PasskeyCredentialRequest request, ClaimsPrincipal principal, UserManager<AppUser> users,
        SignInManager<AppUser> signIn, AuditLog audit, ILoggerFactory loggers)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var result = await signIn.PerformPasskeyAttestationAsync(request.CredentialJson ?? string.Empty);
        if (!result.Succeeded || result.UserEntity.Id != user.Id.ToString())
        {
            LogRejected(loggers.CreateLogger(typeof(AccountEndpoints)), result.Failure?.Message ?? "anderes Konto");
            return ApiProblems.Of(AuthErrors.PasskeyInvalid);
        }

        result.Passkey.Name = string.IsNullOrWhiteSpace(request.Name) ? "Passkey" : request.Name.Trim()[..Math.Min(request.Name.Trim().Length, 100)];
        var added = await users.AddOrUpdatePasskeyAsync(user, result.Passkey);
        if (!added.Succeeded)
        {
            return ApiProblems.Of(AuthErrors.PasskeyInvalid);
        }

        await audit.WriteAsync(AuditActions.PasskeyAdded, user.Id, details: result.Passkey.Name);
        return TypedResults.Ok(new PasskeyDto(Base64Url(result.Passkey.CredentialId), result.Passkey.Name, result.Passkey.CreatedAt));
    }

    private static async Task<IResult> RemoveAsync(string id, ClaimsPrincipal principal, UserManager<AppUser> users, AuditLog audit)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        byte[] credentialId;
        try
        {
            credentialId = FromBase64Url(id);
        }
        catch (FormatException)
        {
            return ApiProblems.Of(AuthErrors.PasskeyInvalid, StatusCodes.Status404NotFound);
        }

        if (await users.GetPasskeyAsync(user, credentialId) is not { } passkey)
        {
            return ApiProblems.Of(AuthErrors.PasskeyInvalid, StatusCodes.Status404NotFound);
        }

        await users.RemovePasskeyAsync(user, credentialId);
        await audit.WriteAsync(AuditActions.PasskeyRemoved, user.Id, details: passkey.Name);
        return TypedResults.NoContent();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Passkey abgelehnt: {Reason}")]
    private static partial void LogRejected(ILogger logger, string reason);

    internal static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '='));
    }
}
