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

    /// <summary>Ausgecheckte Dateien auf diesem Gerät (17).</summary>
    IReadOnlyList<CheckoutInfo> Checkouts { get; }

    event Action? CheckoutsChanged;

    /// <summary>Auschecken und mit der Standard-App öffnen; ist sie schon ausgecheckt, nur öffnen.</summary>
    Task EditAsync(Guid documentId);

    /// <summary>Letzte Änderungen hochladen, Sperre aufheben, lokale Kopie entfernen; false, wenn noch nicht alles oben ist.</summary>
    Task<bool> CheckInAsync(Guid documentId);

    /// <summary>Lokale Änderungen verwerfen und die Sperre aufheben.</summary>
    Task DiscardAsync(Guid documentId);
}

/// <summary>Eine ausgecheckte Datei für die Anzeige.</summary>
public sealed record CheckoutInfo(Guid DocumentId, string Title, string FileName, DateTimeOffset CheckedOutAt, bool Pending, bool Conflict, string? Error);
