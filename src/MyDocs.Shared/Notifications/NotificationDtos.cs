namespace MyDocs.Shared.Notifications;

/// <summary>
/// Benachrichtigungen der eigenen Person (47): Sprache der Mails, Uhrzeit der täglichen Zusammenfassung (null = aus) und
/// optional ein eigenes Thema bei ntfy. Dazu, ob der Administrator Mail und ntfy eingerichtet hat.
/// </summary>
public sealed record NotificationSettingsDto(string Language, int? DigestHour, string? NtfyTopic, bool MailConfigured, bool NtfyConfigured);

public sealed record NotificationSettingsRequest(string Language, int? DigestHour, string? NtfyTopic);

public static class NotificationErrors
{
    public const string Invalid = "notifications-invalid";
    public const string NothingConfigured = "notifications-nothing-configured";
}

public static class MailLanguages
{
    public const string German = "de";
    public const string English = "en";

    public static IReadOnlyList<string> All { get; } = [German, English];
}
