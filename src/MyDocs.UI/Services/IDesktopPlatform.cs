namespace MyDocs.UI.Services;

/// <summary>
/// Was nur der Desktop-Client kann (37, 36): Inhalte über das eigene Schema laden, Dateien mit der Standard-App
/// öffnen, abmelden durch Widerruf des Geräts. Im Browser ist kein Dienst registriert.
/// </summary>
public interface IDesktopPlatform
{
    /// <summary>Adresse für <c>img</c> und <c>iframe</c> zu einem API-Pfad wie <c>api/documents/{id}/preview</c>.</summary>
    string ResourceUrl(string apiPath);

    /// <summary>Lädt eine Version herunter und öffnet sie schreibgeschützt mit der Standard-App.</summary>
    Task OpenAsync(Guid documentId, int? version, string fileName);

    /// <summary>Widerruft das Gerät und zeigt wieder „Verbinden“.</summary>
    Task DisconnectAsync();

    /// <summary>Zur Seite „Verbinden“, etwa wenn die Anmeldung abgelaufen ist.</summary>
    void ShowConnect();
}
