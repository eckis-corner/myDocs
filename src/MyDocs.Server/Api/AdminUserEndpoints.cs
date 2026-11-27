using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Auth;
using MyDocs.Shared.Admin;
using MyDocs.Shared.Household;

namespace MyDocs.Server.Api;

/// <summary>Benutzerverwaltung (4): Nur der Administrator legt Konten an, ändert Rollen, sperrt und setzt den Zugang zurück.</summary>
internal static class AdminUserEndpoints
{
    public static IEndpointRouteBuilder MapAdminUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/users").RequireAuthorization(Roles.Admin);
        group.MapGet("/", ListAsync);
        group.MapPost("/", CreateAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapPost("/{id:guid}/lock", LockAsync);
        group.MapPost("/{id:guid}/unlock", UnlockAsync);
        group.MapPost("/{id:guid}/reset", ResetAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(MyDocsDbContext db, UserManager<AppUser> users, TimeProvider time, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var pending = await db.OneTimeTokens.Where(t => t.Purpose == TokenPurposes.Activation && t.UsedAt == null && t.ExpiresAt > now)
            .GroupBy(t => t.UserId).Select(g => new { UserId = g.Key, ExpiresAt = g.Max(t => t.ExpiresAt) }).ToDictionaryAsync(x => x.UserId, x => x.ExpiresAt, ct);
        var result = new List<AdminUserDto>();
        foreach (var user in await db.Users.OrderBy(u => u.DisplayName).ToListAsync(ct))
        {
            result.Add(await ToDtoAsync(users, user, pending.TryGetValue(user.Id, out var expires) ? expires : null));
        }

        return TypedResults.Ok(result);
    }

    private static async Task<IResult> CreateAsync(CreateUserRequest request, MyDocsDbContext db, UserManager<AppUser> users,
        RoleManager<IdentityRole<Guid>> roles, OneTimeTokens tokens, TimeProvider time, CancellationToken ct)
    {
        if (!MailAddress.TryCreate(request.Email?.Trim(), out var email))
        {
            return ApiProblems.Of(HouseholdErrors.EmailInvalid);
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return ApiProblems.Of(HouseholdErrors.NameRequired);
        }

        if (!HouseholdRoles.All.Contains(request.Role) || (request.IsAdmin && request.Role != HouseholdRoles.Member))
        {
            return ApiProblems.Of(HouseholdErrors.RoleInvalid);
        }

        if (await users.FindByEmailAsync(email.Address) is not null)
        {
            return ApiProblems.Of(HouseholdErrors.EmailTaken, StatusCodes.Status409Conflict);
        }

        var user = new AppUser
        {
            Email = email.Address,
            UserName = email.Address,
            EmailConfirmed = true,
            DisplayName = request.DisplayName.Trim(),
            CreatedAt = time.GetUtcNow(),
        };
        var created = await users.CreateAsync(user);
        if (!created.Succeeded)
        {
            return ApiProblems.Of(HouseholdErrors.EmailInvalid);
        }

        await HouseholdAccounts.EnsureRolesAsync(roles);
        await users.AddToRolesAsync(user, request.IsAdmin ? [request.Role, Roles.Admin] : [request.Role]);
        db.Persons.Add(new Person { Id = Guid.CreateVersion7(), DisplayName = user.DisplayName, UserId = user.Id, CreatedAt = user.CreatedAt });
        await db.SaveChangesAsync(ct);
        var (token, expires) = await tokens.IssueAsync(user.Id, TokenPurposes.Activation, OneTimeTokens.ActivationLifetime, ct);
        return TypedResults.Ok(new ActivationLinkDto(user.Id, token, expires));
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateUserRequest request, ClaimsPrincipal principal, MyDocsDbContext db,
        UserManager<AppUser> users, CancellationToken ct)
    {
        if (await users.FindByIdAsync(id.ToString()) is not { } user)
        {
            return ApiProblems.Of(HouseholdErrors.NotFound, StatusCodes.Status404NotFound);
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return ApiProblems.Of(HouseholdErrors.NameRequired);
        }

        if (!HouseholdRoles.All.Contains(request.Role) || (request.IsAdmin && request.Role != HouseholdRoles.Member))
        {
            return ApiProblems.Of(HouseholdErrors.RoleInvalid);
        }

        var wasAdmin = await users.IsInRoleAsync(user, Roles.Admin);
        if (wasAdmin && !request.IsAdmin)
        {
            if (IsSelf(principal, user))
            {
                return ApiProblems.Of(HouseholdErrors.NotSelf);
            }

            if (await HouseholdAccounts.OtherActiveAdminsAsync(users, user.Id) == 0)
            {
                return ApiProblems.Of(HouseholdErrors.LastAdmin);
            }
        }

        user.DisplayName = request.DisplayName.Trim();
        await users.UpdateAsync(user);
        await db.Persons.Where(p => p.UserId == user.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.DisplayName, user.DisplayName), ct);

        var current = await users.GetRolesAsync(user);
        var wanted = request.IsAdmin ? new[] { request.Role, Roles.Admin } : [request.Role];
        await users.RemoveFromRolesAsync(user, current.Except(wanted));
        await users.AddToRolesAsync(user, wanted.Except(current));
        if (!current.Order().SequenceEqual(wanted.Order()))
        {
            // Neue Rechte gelten sofort: alte Sitzungen müssen sich neu anmelden.
            await users.UpdateSecurityStampAsync(user);
        }

        return TypedResults.Ok(await ToDtoAsync(users, user, null));
    }

    private static async Task<IResult> LockAsync(Guid id, ClaimsPrincipal principal, UserManager<AppUser> users)
    {
        if (await users.FindByIdAsync(id.ToString()) is not { } user)
        {
            return ApiProblems.Of(HouseholdErrors.NotFound, StatusCodes.Status404NotFound);
        }

        if (IsSelf(principal, user))
        {
            return ApiProblems.Of(HouseholdErrors.NotSelf);
        }

        if (await users.IsInRoleAsync(user, Roles.Admin) && await HouseholdAccounts.OtherActiveAdminsAsync(users, user.Id) == 0)
        {
            return ApiProblems.Of(HouseholdErrors.LastAdmin);
        }

        await users.SetLockoutEnabledAsync(user, true);
        await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        await users.UpdateSecurityStampAsync(user);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> UnlockAsync(Guid id, UserManager<AppUser> users)
    {
        if (await users.FindByIdAsync(id.ToString()) is not { } user)
        {
            return ApiProblems.Of(HouseholdErrors.NotFound, StatusCodes.Status404NotFound);
        }

        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        return TypedResults.NoContent();
    }

    /// <summary>Zugang zurücksetzen: Passwort und zweiter Faktor entfallen, alle Sitzungen enden, es gibt einen neuen Aktivierungslink.</summary>
    private static async Task<IResult> ResetAsync(Guid id, ClaimsPrincipal principal, UserManager<AppUser> users, OneTimeTokens tokens, CancellationToken ct)
    {
        if (await users.FindByIdAsync(id.ToString()) is not { } user)
        {
            return ApiProblems.Of(HouseholdErrors.NotFound, StatusCodes.Status404NotFound);
        }

        if (IsSelf(principal, user))
        {
            return ApiProblems.Of(HouseholdErrors.NotSelf);
        }

        if (await users.IsInRoleAsync(user, Roles.Admin) && await HouseholdAccounts.OtherActiveAdminsAsync(users, user.Id) == 0)
        {
            return ApiProblems.Of(HouseholdErrors.LastAdmin);
        }

        if (await users.HasPasswordAsync(user))
        {
            await users.RemovePasswordAsync(user);
        }

        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);
        await users.RemoveAuthenticationTokenAsync(user, "[AspNetUserStore]", "RecoveryCodes");
        await users.UpdateSecurityStampAsync(user);
        var (token, expires) = await tokens.IssueAsync(user.Id, TokenPurposes.Activation, OneTimeTokens.ActivationLifetime, ct);
        return TypedResults.Ok(new ActivationLinkDto(user.Id, token, expires));
    }

    private static bool IsSelf(ClaimsPrincipal principal, AppUser user) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier) == user.Id.ToString();

    private static async Task<AdminUserDto> ToDtoAsync(UserManager<AppUser> users, AppUser user, DateTimeOffset? activationExpires) =>
        new(user.Id, user.Email ?? string.Empty, user.DisplayName, await HouseholdAccounts.HouseholdRoleAsync(users, user),
            await users.IsInRoleAsync(user, Roles.Admin), HouseholdAccounts.StateOf(user), user.CreatedAt, activationExpires);
}
