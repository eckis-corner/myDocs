namespace MyDocs.Shared.Notifications;

/// <summary>
/// Benachrichtigungen der eigenen Person (47): Sprache der Mails, Uhrzeit der täglichen Zusammenfassung (null = aus) und
/// optional ein eigenes Thema bei ntfy. Dazu, ob der Administrator Mail und ntfy eingerichtet hat.
/// </summary>
public sealed record NotificationSettingsDto(string Language, int? DigestHour, string? NtfyTopic, bool MailConfigured, bool NtfyConfigured)
{
    /// <summary>Weitere Absenderadressen für den Mail-Eingang (49).</summary>
    public IReadOnlyList<string> MailSenders { get; init; } = [];

    public bool MailBodyAlways { get; init; }

    /// <summary>Adresse des Mail-Eingangs (das IMAP-Konto), wenn eingerichtet.</summary>
    public string? MailInAddress { get; init; }
}

public sealed record NotificationSettingsRequest(string Language, int? DigestHour, string? NtfyTopic)
{
    /// <summary>null lässt die Absenderadressen unverändert.</summary>
    public IReadOnlyList<string>? MailSenders { get; init; }

    public bool MailBodyAlways { get; init; }
}

/// <summary>Grenzen des Mail-Eingangs.</summary>
public static class MailIntakeLimits
{
    public const int MaxSenders = 5;
}

public static class NotificationErrors
{
    public const string Invalid = "notifications-invalid";
    public const string NothingConfigured = "notifications-nothing-configured";
    public const string InvalidSender = "notifications-invalid-sender";
}

public static class MailLanguages
{
    public const string German = "de";
    public const string English = "en";

    public static IReadOnlyList<string> All { get; } = [German, English];
}
