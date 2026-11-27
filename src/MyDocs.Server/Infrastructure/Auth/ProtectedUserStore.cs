using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using MyDocs.Server.Data;

namespace MyDocs.Server.Infrastructure.Auth;

/// <summary>
/// Benutzerspeicher, der alle Tokens verschlüsselt ablegt (ASP.NET Data Protection, Schlüssel in <c>data/keys</c>).
/// Identity speichert dort das TOTP-Geheimnis und die Wiederherstellungscodes, sonst im Klartext in der Datenbank.
/// </summary>
public sealed class ProtectedUserStore(MyDocsDbContext context, IDataProtectionProvider protection, IdentityErrorDescriber? describer = null)
    : UserStore<AppUser, IdentityRole<Guid>, MyDocsDbContext, Guid>(context, describer)
{
    private readonly IDataProtector _protector = protection.CreateProtector("myDocs.UserTokens.v1");

    public override Task SetTokenAsync(AppUser user, string loginProvider, string name, string? value, CancellationToken cancellationToken) =>
        base.SetTokenAsync(user, loginProvider, name, value is null ? null : _protector.Protect(value), cancellationToken);

    public override async Task<string?> GetTokenAsync(AppUser user, string loginProvider, string name, CancellationToken cancellationToken)
    {
        var value = await base.GetTokenAsync(user, loginProvider, name, cancellationToken);
        return value is null ? null : _protector.Unprotect(value);
    }
}
