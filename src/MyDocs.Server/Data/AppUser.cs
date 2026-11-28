using Microsoft.AspNetCore.Identity;

namespace MyDocs.Server.Data;

/// <summary>Benutzer mit Login (4). Nur der Administrator legt Benutzer an; der erste entsteht im Einrichtungsassistenten.</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Letzter angenommener TOTP-Zeitschritt; jeder Code gilt nur einmal.</summary>
    public long LastTotpStep { get; set; }

    /// <summary>Sprache der Mails (de, en).</summary>
    public string Language { get; set; } = "de";

    /// <summary>Stunde der täglichen Zusammenfassung in Ortszeit; null = keine (47).</summary>
    public int? DigestHour { get; set; } = 7;

    /// <summary>Eigenes Thema bei ntfy; null = keine Push-Nachrichten.</summary>
    public string? NtfyTopic { get; set; }

    /// <summary>Tag der letzten Zusammenfassung (Ortszeit), damit sie einmal am Tag kommt.</summary>
    public DateOnly? LastDigestOn { get; set; }

    /// <summary>Weitere Absenderadressen für den Mail-Eingang (49), neben der Anmeldeadresse.</summary>
    public List<string> MailSenders { get; set; } = [];

    /// <summary>Mailtext immer als Dokument, auch wenn die Mail Anhänge hat.</summary>
    public bool MailBodyAlways { get; set; }
}

public static class Roles
{
    public const string Admin = "admin";
}
