namespace MyDocs.Server.Infrastructure;

public sealed class MyDocsOptions
{
    public const string SectionName = "MyDocs";

    /// <summary>Wurzelverzeichnis für Dateien, Sicherungen und Schlüssel (im Container /data).</summary>
    public string DataDirectory { get; set; } = "data-local";

    /// <summary>Netze, deren X-Forwarded-*-Header angenommen werden (Reverse Proxy). Standard: private Netze.</summary>
    public IList<string> TrustedProxyNetworks { get; set; } = ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8", "::1/128"];

    /// <summary>Anfragen je Minute und IP an Anmeldung und Einrichtung.</summary>
    public int AuthRequestsPerMinute { get; set; } = 30;

    /// <summary>Größte Datei in Megabyte (78); später vom Administrator einstellbar.</summary>
    public int MaxFileSizeMegabytes { get; set; } = 100;
}
