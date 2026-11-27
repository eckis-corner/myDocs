using Microsoft.AspNetCore.Identity;
using MyDocs.Server.Data;

namespace MyDocs.Server.Infrastructure.Auth;

/// <summary>
/// Authenticator-Codes wie bei myLife: Fenster ±30 Sekunden, und **jeder Code gilt nur einmal**. Der Standard von
/// Identity nimmt denselben Code innerhalb seines Fensters mehrfach an.
/// </summary>
public sealed class OneTimeTotpProvider(TimeProvider time) : IUserTwoFactorTokenProvider<AppUser>
{
    public const string Name = "MyDocsTotp";

    /// <summary>
    /// „Kann erzeugen“ heißt für Identity: Der zweite Faktor steht dem Benutzer zur Verfügung. Ohne das verlangt die
    /// Anmeldung keinen zweiten Faktor.
    /// </summary>
    public async Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<AppUser> manager, AppUser user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return !string.IsNullOrWhiteSpace(await manager.GetAuthenticatorKeyAsync(user));
    }

    public Task<string> GenerateAsync(string purpose, UserManager<AppUser> manager, AppUser user) =>
        throw new NotSupportedException("Codes erzeugt die Authenticator-App.");

    public async Task<bool> ValidateAsync(string purpose, string token, UserManager<AppUser> manager, AppUser user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(user);
        var key = await manager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key) || Totp.Verify(Base32.Decode(key), token, time.GetUtcNow()) is not { } step || step <= user.LastTotpStep)
        {
            return false;
        }

        user.LastTotpStep = step;
        return (await manager.UpdateAsync(user)).Succeeded;
    }
}
