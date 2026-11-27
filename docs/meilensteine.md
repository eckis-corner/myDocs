# myDocs – Umsetzungsplan

Stand: 27.11.2026. Nach jedem Meilenstein wird angehalten und abgenommen. Die Nummern verweisen auf das [Entscheidungsprotokoll](entscheidungen.md).

## M0 – my-common herauslösen (51–55, 69–73)

**M0.1 Katalog ✅ (27.11.2026, lokal):** `MyCommon.Catalog` mit Schema, Katalog, Feldprüfungen und `catalog-texts.json`; myLife auf Branch `m0-my-common` umgestellt, 283 von 284 Tests grün, 1 übersprungen, wie vorher. Ressourcen von myLife unverändert.

**M0.2 Oberfläche ✅ (27.11.2026, lokal):** `MyCommon.Ui` mit Theme, Schrift, `CatalogText` und `CatalogFields` (74, 75); myLife nutzt es, 283 von 284 Tests grün, 1 übersprungen, wie vorher. Offen für die Abnahme: Release myLife 1.2 nach dem Merge von `m0-my-common`.


- Repo `eckis-corner/my-common`, AGPL-3.0, CI veröffentlicht die Pakete nach GitHub Packages
- `MyCommon.Catalog`: Schema, `ObjectCatalog`, Auswahllisten, Feldprüfungen, Ressourcen. Neu: `MyLifeRelevant`, Lebenszyklus-Muster, `ReviewRule`
- `MyCommon.Ui`: Theme mit Akzentfarbe je App, AppLogo, LanguageMenu, UserMenu, Grundlayout, ObjectForm und ObjectDatasheet **entkoppelt von der myLife-Krypto**
- myLife auf die Pakete umstellen, bedingte `ProjectReference` für die lokale Entwicklung
- Logo und Akzentfarbe für myDocs

**Abnahme:** Alle Tests von myLife sind grün, und myLife 1.2 verhält sich wie 1.1.

## M1 – Grundgerüst myDocs

**M1.1 Leeres Grundgerüst ✅ (27.11.2026, lokal):** Solution mit Shared, ApiClient, UI, Web, Server und Tests; Layout wie myLife in Petrol, Logo, Sprachen de/en, PostgreSQL mit erster Migration, `/health`, `/api/info`, Sicherheits-Header, Docker, CI, VS Code. 13 Tests grün.

**M1.2 Einrichtung und Anmeldung ✅ (27.11.2026, lokal):** Einrichtungsassistent mit Code, Administrator, TOTP und Wiederherstellungscodes; Anmeldung mit Passwort und Einmal-Code, Abmelden, Benutzermenü; CSRF-Header, Sperre, Rate-Limit; Test, dass jeder API-Endpunkt eine Anmeldung verlangt (80–84). 26 Tests grün, Ablauf lokal gegen PostgreSQL durchgespielt.

**M1.3 Benutzer, Personen, Haushalt ✅ (27.11.2026, lokal):** Benutzerverwaltung mit Aktivierungslink, Rollen Mitglied, Gast und Administrator, Sperren und Zurücksetzen, Seite „Haushalt“ mit Personen (85–90). 37 Tests grün, Migration gegen den Stand aus M1.2 durchgespielt.

**M1.4 Objekte ✅ (27.11.2026, lokal):** Bereiche in der Navigation, Liste je Bereich mit Filter, Objektseite mit Datenblatt und Formular (CatalogFields aus MyCommon.Ui), Ablageorte, Haushalt oder privat, Zuständigkeit, Freigaben, Konflikterkennung, Historie, Papierkorb (91–96). 49 Tests grün.

**M1.5 Dokumente ✅ (27.11.2026, lokal):** Hochladen an Objekten, verschlüsselte Ablage mit Hauptschlüssel, Duplikaterkennung, Versionen, Vorschau für PDF und Bilder, Download, Angaben, Zuordnung zu mehreren Objekten, Papierkorb (97–101). 61 Tests grün, 40 MB lokal hoch- und heruntergeladen.

**M1.6 Suche ✅ (27.11.2026, lokal):** Volltext mit Wortstämmen, Teilwörter, Tippfehler, Auswahlwerte in beiden Sprachen, Dokumentnamen, Rechte in der Abfrage, Suchfeld und Seite „Suche“ (102–104). 69 Tests grün.

**M1.7a Protokoll, Admin-Einsicht, Papierkorb ✅ (27.11.2026, lokal):** Audit-Log mit Seiten für jede Person und den Administrator, Admin-Einsicht nur zum Lesen mit Band und Protokoll für den Eigentümer, tägliches Leeren des Papierkorbs (105–109). 79 Tests grün.

**M1.7b Sicherung und Betrieb:** tägliche verschlüsselte Sicherung, `restore`, Hauptschlüssel als 24 Wörter, Installationsanleitung.

**M1.2b Passkeys:** als zweiter Faktor statt TOTP, mit den Passkeys von ASP.NET Core Identity 10.



1. Solution `MyDocs.slnx` mit den Projekten aus [architektur.md](architektur.md), Abschnitt 3; Docker Compose mit `app` und `db`; CI mit Image nach `ghcr.io/eckis-corner/mydocs`
2. Einrichtungsassistent mit Einrichtungscode, erster Admin, 2FA (TOTP und Passkey)
3. Admin legt Benutzer an, Personen-Stammdaten, Haushalt, Rollen Mitglied und Gast
4. Objekte aus dem Katalog: anlegen, bearbeiten, Besitz, Zuständigkeit, private Freigaben, Admin-Einsicht, Historie, Papierkorb
5. Dokumente: Upload, verschlüsselte Blobs adressiert über ihren Inhalts-Hash, Versionen, Zuordnung n:m, Download, Vorschau von PDF und Bildern
6. Suche über Titel und Felder (PostgreSQL), Rechte in der Abfrage
7. Audit-Log, Admin-Einsicht (96), Papierkorb nach 30 Tagen leeren, Sicherheits-Header, Sicherung und `restore`, Hauptschlüssel als Ausdruck
8. Installationsanleitung für Synology und Nginx Proxy Manager

**Abnahme:** Die App läuft unter `https://mydocs.eckis-corner.de`, eine Rücksicherung ist getestet, und auf dem Volume liegt nachweislich nur Chiffrat.

## M2 – Arbeiten am Bestand (8–16, 47, 50, 65)

1. Lebenszyklus mit drei Mustern und Folgevorgängen
2. Vorgänge (alle Arten, manuell und automatisch), Kommentare
3. Prüfregeln aus dem Katalog und je Objekt, `ReviewService`, Eskalation, „überfällig“
4. Prüfung mit Ergebnis als Pflicht, Zurückstellen mit Obergrenze
5. Dashboard mit fünf Blöcken, „Meins“ / „Haushalt“
6. SMTP und ntfy, tägliche Zusammenfassung

**Abnahme:** Eigene Abnahme auf der Synology. **Ab hier echte Daten (67).**

## M3 – Posteingang und Worker (19–26, 49)

1. Container `mydocs-worker`, Auftragstabelle, OCRmyPDF zu PDF/A, LibreOffice-Vorschau, Vorschaubilder
2. Volltextsuche über den Dokumenttext (`german`, `pg_trgm`)
3. Posteingang mit Zuordnung, Dokumentart und Datum
4. Kamera-Upload (Seiten sammeln, sortieren, zuschneiden)
5. Scan-Ordner auf der Synology
6. IMAP mit Absenderprüfung, Positivliste, Markierung von Makros

**Abnahme:** Ein Brief, der per Kamera, Scanner und Mail hereinkommt, ist jedes Mal durchsuchbar und zugeordnet.

## M4 – Desktop-Client (17, 36–38, 41)

1. `MyDocs.Desktop` mit Photino.Blazor und `MyDocs.UI`
2. Anmeldung mit PKCE und Loopback, Gerätetoken im Schlüsselspeicher, Geräteverwaltung in der Web-App
3. Check-out/Check-in mit File-Watcher, Sperren, Konfliktversion, offline weiterarbeiten
4. Eingangsordner, Drag & Drop, Tray, Benachrichtigungen
5. Builds für Windows, macOS und Linux in der CI

**Abnahme:** Eine Excel-Liste lässt sich auf allen drei Systemen öffnen, ändern und speichern, und jede Speicherung ist eine Version.

## M5 – Brücke zu myLife (56–64)

1. myDocs: lesendes Token, Export-Endpunkte, CORS, Vorgang „myLife abgleichen“
2. myLife: Seite „Abgleich mit myDocs“, Verknüpfung über `myDocsId`, Schreibschutz für Felder aus myDocs, Dokumente als feste Kopie, Anzeige „Stand ungeprüft seit …“
3. Objekt „myDocs-Wiederherstellung“ in myLife

**Abnahme:** Eine Änderung an einer Versicherung in myDocs steht nach dem Abgleich verschlüsselt in myLife, und der myLife-Server sieht nur Chiffrat.

## M6 – Komfort (20, 28, 62)

1. Übersicht „Zahlungen“ mit Haushaltssicht und Filter „gekündigt zum …“
2. Aufbewahrungsfristen je Dokumentart, Vorgang „Aufräumen“
3. Zuordnungsvorschläge aus der OCR

## Später

- WebDAV als zweiter Weg für Office (17)
- Offline lesen im Desktop-Client (38)
- Einzelne Objekte clientseitig verschlüsseln (2)
- Ausdünnen alter Versionen (27)
- ClamAV, wenn RAM verfügbar ist (49)
