namespace MyDocs.Shared;

/// <summary>Öffentliche Basisinformationen der Instanz (ohne Umgebungs- oder Konfigurationsdetails).</summary>
/// <param name="Version">Version der Anwendung.</param>
/// <param name="MaxFileSizeMegabytes">Größte Datei, die der Server annimmt (78).</param>
public sealed record AppInfoDto(string Version, int MaxFileSizeMegabytes = 100);
