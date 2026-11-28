using System.Net.Mail;
using System.Text.RegularExpressions;

namespace MyDocs.Shared.Admin;

/// <summary>Verbindung zum SMTP-Server.</summary>
public enum SmtpSecurity
{
    /// <summary>STARTTLS, wenn der Server es anbietet (Port 587).</summary>
    StartTls = 0,

    /// <summary>TLS von Anfang an (Port 465).</summary>
    SslOnConnect = 1,

    /// <summary>Unverschlüsselt, nur im eigenen Netz.</summary>
    None = 2,
}

/// <summary>SMTP für den Versand (47), getrennt von myLife. Das Passwort geht nie an den Browser: null behält es, leer löscht es.</summary>
public sealed record SmtpSettingsDto
{
    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 587;

    public SmtpSecurity Security { get; init; }

    public string? Username { get; init; }

    public string? Password { get; init; }

    public bool HasPassword { get; init; }

    public string FromAddress { get; init; } = string.Empty;

    public string FromName { get; init; } = "myDocs";
}

/// <summary>
/// IMAP-Postfach für den Mail-Eingang (49, 148), getrennt vom SMTP-Konto. Passwort wie bei SMTP. Ist
/// <see cref="TrustedAuthServId"/> gesetzt, zählen nur <c>Authentication-Results</c> dieses Mailservers.
/// </summary>
public sealed record ImapSettingsDto
{
    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 993;

    public SmtpSecurity Security { get; init; } = SmtpSecurity.SslOnConnect;

    public string? Username { get; init; }

    public string? Password { get; init; }

    public bool HasPassword { get; init; }

    public string Folder { get; init; } = "INBOX";

    /// <summary>Kennung des eigenen Mailservers in <c>Authentication-Results</c>, etwa <c>mx.example.org</c>.</summary>
    public string? TrustedAuthServId { get; init; }
}

/// <summary>ntfy für Push-Nachrichten (47), optional. Token wie das SMTP-Passwort.</summary>
public sealed record NtfySettingsDto
{
    public string? ServerUrl { get; init; }

    public string? AccessToken { get; init; }

    public bool HasAccessToken { get; init; }
}

/// <summary>Tägliche Sicherung. Der Sicherungsschlüssel bleibt in der <c>.env</c>.</summary>
public sealed record BackupSettingsDto
{
    public int Hour { get; init; } = 3;

    public int KeepDaily { get; init; } = 7;

    public int KeepWeekly { get; init; } = 4;

    public int KeepMonthly { get; init; } = 6;

    public bool IncludeDocuments { get; init; } = true;
}

/// <summary>Einstellungen des Administrators (Verwaltung → Einstellungen).</summary>
public sealed record AdminSettingsDto
{
    /// <summary>Adresse der Instanz für Links in Mails, etwa https://mydocs.example.org.</summary>
    public string? PublicUrl { get; init; }

    public SmtpSettingsDto Smtp { get; init; } = new();

    public NtfySettingsDto Ntfy { get; init; } = new();

    public ImapSettingsDto Imap { get; init; } = new();

    /// <summary>Größte Datei in MB (78), höchstens <see cref="FileSizeCeilingMegabytes"/>.</summary>
    public int MaxFileSizeMegabytes { get; init; } = 100;

    public BackupSettingsDto Backup { get; init; } = new();

    /// <summary>Obergrenze aus der Umgebung (<c>MyDocs__MaxFileSizeMegabytes</c>); nur zur Anzeige, wird beim Speichern ignoriert.</summary>
    public int FileSizeCeilingMegabytes { get; init; } = 100;
}

public sealed record NtfyTestRequest(string Topic);

public static class SettingsErrors
{
    public const string Url = "settings-url";
    public const string Range = "settings-range";
    public const string Required = "settings-required";
    public const string Email = "settings-email";
    public const string TooLong = "settings-too-long";
    public const string Topic = "settings-topic";
    public const string MailNotConfigured = "mail-not-configured";
    public const string NtfyNotConfigured = "ntfy-not-configured";
    public const string ImapNotConfigured = "imap-not-configured";
    public const string SendFailed = "send-failed";
}

/// <summary>Prüft die Einstellungen, gleich in Browser und Server. Ergebnis: Feld → Fehlercode; leer, wenn alles stimmt.</summary>
public static partial class AdminSettingsValidation
{
    public static Dictionary<string, string> Validate(AdminSettingsDto settings, int fileSizeCeiling)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(settings.PublicUrl) && !IsHttpUrl(settings.PublicUrl))
        {
            errors["publicUrl"] = SettingsErrors.Url;
        }

        var smtp = settings.Smtp ?? new SmtpSettingsDto();
        if (!string.IsNullOrWhiteSpace(smtp.Host))
        {
            if (smtp.Host.Trim().Length > 253 || smtp.Host.Trim().Contains(' ', StringComparison.Ordinal))
            {
                errors["smtp.host"] = SettingsErrors.Url;
            }

            if (smtp.Port is < 1 or > 65535)
            {
                errors["smtp.port"] = SettingsErrors.Range;
            }

            if (string.IsNullOrWhiteSpace(smtp.FromAddress))
            {
                errors["smtp.fromAddress"] = SettingsErrors.Required;
            }
            else if (!IsEmail(smtp.FromAddress))
            {
                errors["smtp.fromAddress"] = SettingsErrors.Email;
            }

            if (smtp.FromName is { Length: > 100 } || smtp.Username is { Length: > 254 } || smtp.Password is { Length: > 500 })
            {
                errors["smtp.fromName"] = SettingsErrors.TooLong;
            }
        }

        var imap = settings.Imap ?? new ImapSettingsDto();
        if (!string.IsNullOrWhiteSpace(imap.Host))
        {
            if (imap.Host.Trim().Length > 253 || imap.Host.Trim().Contains(' ', StringComparison.Ordinal))
            {
                errors["imap.host"] = SettingsErrors.Url;
            }

            if (imap.Port is < 1 or > 65535)
            {
                errors["imap.port"] = SettingsErrors.Range;
            }

            if (string.IsNullOrWhiteSpace(imap.Username))
            {
                errors["imap.username"] = SettingsErrors.Required;
            }

            if (string.IsNullOrWhiteSpace(imap.Folder) || imap.Folder.Length > 200 || imap.Username is { Length: > 254 } || imap.Password is { Length: > 500 }
                || imap.TrustedAuthServId is { Length: > 253 })
            {
                errors["imap.folder"] = SettingsErrors.TooLong;
            }
        }

        var ntfy = settings.Ntfy ?? new NtfySettingsDto();
        if (!string.IsNullOrWhiteSpace(ntfy.ServerUrl) && !IsHttpUrl(ntfy.ServerUrl))
        {
            errors["ntfy.serverUrl"] = SettingsErrors.Url;
        }

        if (ntfy.AccessToken is { Length: > 500 })
        {
            errors["ntfy.accessToken"] = SettingsErrors.TooLong;
        }

        if (settings.MaxFileSizeMegabytes < 1 || settings.MaxFileSizeMegabytes > fileSizeCeiling)
        {
            errors["maxFileSize"] = SettingsErrors.Range;
        }

        var backup = settings.Backup ?? new BackupSettingsDto();
        if (backup.Hour is < 0 or > 23)
        {
            errors["backup.hour"] = SettingsErrors.Range;
        }

        if (backup.KeepDaily is < 1 or > 60 || backup.KeepWeekly is < 0 or > 52 || backup.KeepMonthly is < 0 or > 120)
        {
            errors["backup.keep"] = SettingsErrors.Range;
        }

        return errors;
    }

    public static bool IsEmail(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 254 && MailAddress.TryCreate(value.Trim(), out var address) && address.Address == value.Trim();

    public static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);

    /// <summary>Thema bei ntfy: Buchstaben, Ziffern, - und _, höchstens 64 Zeichen.</summary>
    public static bool IsTopic(string? value) => value is not null && TopicPattern().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex TopicPattern();
}
