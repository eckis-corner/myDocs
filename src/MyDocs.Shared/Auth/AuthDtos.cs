namespace MyDocs.Shared.Auth;

/// <summary>Regeln für Passwörter, gleich im Browser und auf dem Server.</summary>
public static class PasswordRules
{
    public const int MinLength = 12;
}

/// <summary>Fehlercodes der Anmeldung und Einrichtung; Texte unter <c>AuthError_{Code}</c>.</summary>
public static class AuthErrors
{
    public const string Invalid = "auth-invalid";
    public const string Locked = "auth-locked";
    public const string CodeInvalid = "auth-code-invalid";
    public const string TwoFactorMissing = "auth-2fa-missing";
    public const string SetupDone = "setup-done";
    public const string SetupCodeInvalid = "setup-code-invalid";
    public const string PasswordWeak = "password-weak";
    public const string EmailInvalid = "email-invalid";
}

public sealed record SetupStatusDto(bool Required);

/// <summary>Schritt 1 der Einrichtung: Einrichtungscode aus dem Protokoll, erstes Konto (wird Administrator).</summary>
public sealed record SetupStartRequest(string SetupCode, string Email, string DisplayName, string Password);

/// <summary>Schlüssel für die Authenticator-App (Base32) und die otpauth-Adresse für den QR-Code.</summary>
public sealed record TotpSetupDto(string Secret, string OtpAuthUri);

/// <summary>Schritt 2: erster Code aus der Authenticator-App. Danach ist das Konto aktiv und angemeldet.</summary>
public sealed record SetupFinishRequest(string SetupCode, string Email, string Code);

/// <summary>Einmal-Codes für den Fall, dass das Gerät mit der Authenticator-App verloren geht.</summary>
public sealed record RecoveryCodesDto(IReadOnlyList<string> Codes);

public sealed record LoginRequest(string Email, string Password);

public sealed record LoginResultDto(bool TwoFactorRequired);

/// <summary>Zweiter Faktor: Code der Authenticator-App oder ein Wiederherstellungscode.</summary>
public sealed record LoginTwoFactorRequest(string Code, bool IsRecoveryCode);

public sealed record MeDto(Guid Id, string Email, string DisplayName, bool IsAdmin, int RecoveryCodesLeft);
