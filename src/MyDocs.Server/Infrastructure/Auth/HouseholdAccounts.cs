using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyDocs.Server.Data;
using MyDocs.Shared.Admin;
using MyDocs.Shared.Household;

namespace MyDocs.Server.Infrastructure.Auth;

/// <summary>Rollen und Zustände der Konten im Haushalt (32).</summary>
internal static class HouseholdAccounts
{
    public static async Task EnsureRolesAsync(RoleManager<IdentityRole<Guid>> roles)
    {
        foreach (var role in new[] { Roles.Admin, HouseholdRoles.Member, HouseholdRoles.Guest })
        {
            if (!await roles.RoleExistsAsync(role))
            {
                await roles.CreateAsync(new IdentityRole<Guid>(role));
            }
        }
    }

    /// <summary>Gesperrt ist, wer bis auf Weiteres gesperrt wurde (nicht die kurze Sperre nach Fehlversuchen).</summary>
    public static bool IsLocked(AppUser user) => user.LockoutEnd is { } end && end > DateTimeOffset.UtcNow.AddYears(50);

    public static string StateOf(AppUser user) =>
        IsLocked(user) ? UserStates.Locked : user.TwoFactorEnabled ? UserStates.Active : UserStates.Pending;

    /// <summary>Anzahl der aktiven Administratoren ohne <paramref name="except"/>.</summary>
    public static async Task<int> OtherActiveAdminsAsync(UserManager<AppUser> users, Guid except)
    {
        var admins = await users.GetUsersInRoleAsync(Roles.Admin);
        return admins.Count(a => a.Id != except && a.TwoFactorEnabled && !IsLocked(a));
    }

    public static async Task<string> HouseholdRoleAsync(UserManager<AppUser> users, AppUser user) =>
        await users.IsInRoleAsync(user, HouseholdRoles.Member) ? HouseholdRoles.Member : HouseholdRoles.Guest;

    public static Task<Guid?> PersonIdAsync(MyDocsDbContext db, Guid userId, CancellationToken ct) =>
        db.Persons.Where(p => p.UserId == userId).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
}
