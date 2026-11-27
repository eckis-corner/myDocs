namespace MyDocs.Server.Data;

/// <summary>Globale Einstellung als Schlüssel und Wert (JSON), etwa SMTP oder Grenzwerte.</summary>
public sealed class SystemSetting
{
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }
}
