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

    /// <summary>
    /// Sicherungsschlüssel: 32 Byte als Base64, aus <c>MYDOCS_BACKUP_KEY</c> in der <c>.env</c>. Ohne ihn gibt es keine
    /// automatische Sicherung. Er gehört getrennt von den Sicherungen aufbewahrt.
    /// </summary>
    public string? BackupKey { get; set; }

    /// <summary>Uhrzeit der täglichen Sicherung (Ortszeit, volle Stunde).</summary>
    public int BackupHour { get; set; } = 3;

    /// <summary>Generationen: je Tag, je Woche, je Monat die neueste Sicherung.</summary>
    public int BackupKeepDaily { get; set; } = 7;

    public int BackupKeepWeekly { get; set; } = 4;

    public int BackupKeepMonthly { get; set; } = 6;

    /// <summary>Dokumente mitsichern (verschlüsselt, wie sie im Volume liegen).</summary>
    public bool BackupIncludeDocuments { get; set; } = true;

    /// <summary>Größte Datei in Megabyte (78); später vom Administrator einstellbar.</summary>
    public int MaxFileSizeMegabytes { get; set; } = 100;

    /// <summary>
    /// Adresse des Workers im internen Netz (24), etwa <c>http://worker:8080</c>. Leer: Dokumente werden gespeichert,
    /// aber nicht verarbeitet, bis ein Worker da ist.
    /// </summary>
    public string? WorkerUrl { get; set; }

    /// <summary>
    /// Scan-Ordner (147), im Container <c>/scan</c>. Leer: kein Scan-Ordner. Dateien darin gehen in den Posteingang des
    /// Haushalts, in Unterordnern mit der E-Mail-Adresse eines Kontos in dessen eigenen.
    /// </summary>
    public string? ScanDirectory { get; set; }

    /// <summary>
    /// Adresse von myLife (56, 166), etwa <c>https://mylife.eckis-corner.de</c>, aus <c>MYDOCS_MYLIFE_URL</c>. Nur dieser
    /// Origin darf die Brücke unter <c>/api/mylife</c> aus dem Browser abfragen. Leer: keine Brücke.
    /// </summary>
    public string? MyLifeUrl { get; set; }

    /// <summary>Origin einer Adresse (<c>https://host[:port]</c>, ohne Pfad); null, wenn sie fehlt oder kein http(s) ist.</summary>
    public static string? Origin(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri.GetLeftPart(UriPartial.Authority)
            : null;
}
