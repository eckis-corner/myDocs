using Microsoft.AspNetCore.Identity;
using MyDocs.Server.Data;
using MyDocs.Shared.Auth;

namespace MyDocs.Server.Infrastructure.Auth;

/// <summary>Einrichten der Authenticator-App, gleich für den Einrichtungsassistenten und die Aktivierung eines Kontos (81).</summary>
internal static class TwoFactorEnrollment
{
    private const string Issuer = "myDocs";

    public static async Task<TotpSetupDto> StartAsync(UserManager<AppUser> users, AppUser user)
    {
        await users.ResetAuthenticatorKeyAsync(user);
        var secret = (await users.GetAuthenticatorKeyAsync(user))!;
        return new TotpSetupDto(secret, OtpAuthUri(user.Email ?? string.Empty, secret));
    }

    /// <summary>Prüft den ersten Code, schaltet den zweiten Faktor ein und liefert die Wiederherstellungscodes; null bei falschem Code.</summary>
    public static async Task<RecoveryCodesDto?> FinishAsync(UserManager<AppUser> users, AppUser user, string? code)
    {
        if (!await users.VerifyTwoFactorTokenAsync(user, OneTimeTotpProvider.Name, code ?? string.Empty))
        {
            return null;
        }

        await users.SetTwoFactorEnabledAsync(user, true);
        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10) ?? [];
        return new RecoveryCodesDto([.. codes]);
    }

    /// <summary>otpauth-URI für Authenticator-Apps (RFC 6238, SHA-1, 6 Ziffern, 30 Sekunden).</summary>
    internal static string OtpAuthUri(string account, string secret) =>
        $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(account)}" +
        $"?secret={secret}&issuer={Uri.EscapeDataString(Issuer)}&algorithm=SHA1&digits=6&period=30";
}
