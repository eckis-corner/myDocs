namespace MyDocs.Shared.Admin;

/// <summary>Stand der Verarbeitung (Verwaltung → Einstellungen): Worker eingerichtet und erreichbar, Aufträge je Zustand.</summary>
public sealed record ProcessingStatusDto(bool Configured, bool Reachable, int Pending, int Failed, int Done);
