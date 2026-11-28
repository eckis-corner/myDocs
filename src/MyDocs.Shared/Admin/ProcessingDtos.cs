namespace MyDocs.Shared.Admin;

/// <summary>Stand der Verarbeitung (Verwaltung → Einstellungen): Worker eingerichtet und erreichbar, Aufträge je Zustand.</summary>
public sealed record ProcessingStatusDto(bool Configured, bool Reachable, int Pending, int Failed, int Done)
{
    /// <summary>Der Scan-Ordner ist eingerichtet und vorhanden (M3.5).</summary>
    public bool ScanFolderActive { get; init; }
}
