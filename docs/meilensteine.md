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

**M1.7b Sicherung und Betrieb ✅ (27.11.2026, lokal):** tägliche verschlüsselte Sicherung, `restore` und `master-key restore` auf der Kommandozeile, Hauptschlüssel als 24 Wörter zum Drucken, Installationsanleitung und Abnahme-Checkliste (110–113). 88 Tests grün, Sichern und Zurückspielen lokal durchgespielt.

**M1.2b Passkeys ✅ (27.11.2026, lokal):** Passkey statt Code nach dem Passwort, Seite „Sicherheit“ (114, 115). 95 Tests grün, vollständige WebAuthn-Zeremonie über einen Authenticator in Software.

**M1 fertig (27.11.2026).** Offen bis zur Abnahme: die Checkliste [betrieb/abnahme.md](betrieb/abnahme.md) auf der Synology. Nicht Teil von M1 und auf später verschoben: Einstellungsseite für den Administrator (Dateigröße, Sicherungszeit; bis dahin Umgebungsvariablen, 78, 110), Pager auch oben an den Tabellen, PNG-Icons für Smartphone und Favicon.



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

**M2.1 Lebenszyklus, Vorgänge, Kommentare ✅ (27.11.2026, lokal):** drei Muster mit Folgevorgängen, Archiv, Vorgänge mit Status und Ergebnis, Seite „Vorgänge“, Kommentare (116–120). 107 Tests grün.

**M2.2 Wiedervorlage ✅ (27.11.2026, lokal):** Prüfregeln je Typ und je Eintrag, Fristen aus Hauptfälligkeit und Kündigungsfrist, Ablauf von Ausweisen und Karten, Eskalation in drei Stufen mit „überfällig“, Ergebnis als Pflicht, Zurückstellen mit Obergrenze (121–125). 126 Tests grün.

**M2.3 Startseite ✅ (27.11.2026, lokal):** fünf Blöcke mit „Meins“/„Haushalt“: fällig, Fristen, Zahlungen je Monat, Posteingang, Zustand des Bestands (126–128). 130 Tests grün.

**M2.4 Benachrichtigungen und Einstellungen ✅ (27.11.2026, lokal):** Seite „Einstellungen“ für SMTP, ntfy, Adresse, Dateigröße und Sicherung; Mail-Outbox; Seite „Benachrichtigungen“ je Person; tägliche Zusammenfassung, sofortige Meldung bei Stufe 3, Aktivierungslink per Mail (129–135). 138 Tests grün. **Damit ist M2 fertig.**

1. Lebenszyklus mit drei Mustern und Folgevorgängen
2. Vorgänge (alle Arten, manuell und automatisch), Kommentare
3. Prüfregeln aus dem Katalog und je Objekt, `ReviewService`, Eskalation, „überfällig“
4. Prüfung mit Ergebnis als Pflicht, Zurückstellen mit Obergrenze
5. Dashboard mit fünf Blöcken, „Meins“ / „Haushalt“
6. SMTP und ntfy, tägliche Zusammenfassung

**Abnahme:** Eigene Abnahme auf der Synology. **Ab hier echte Daten (67).**

## M3 – Posteingang und Worker (19–26, 49)

**M3.1 Worker ✅ (28.11.2026):** Container `mydocs-worker` ohne Schlüssel im internen Netz, Aufträge je Inhalt, OCR zu PDF/A, Office-Vorschau, Vorschaubilder, Wiederholung und Status in den Einstellungen (136–140). Mit den echten Werkzeugen geprüft (Office, Bild, Scan-PDF), auch in der CI.

**M3.2 Volltext über den Dokumenttext ✅ (28.11.2026):** erkannter Text mit eigenem Index, Wortstämme und Teilwörter, Fundstelle in den Treffern (141). 158 Tests grün, dazu 3 gegen den echten Worker.

**M3.3 Posteingang ✅ (28.11.2026):** privater und gemeinsamer Posteingang, Zuordnen mit Dokumentart, Datum, Einträgen und Vorgang, Weitergeben, Verwerfen, fällig nach 7 Tagen, Startseite und Suche (142–145). 170 Tests grün.

**M3.4 Kamera ✅ (28.11.2026):** Seiten fotografieren oder wählen, sortieren, drehen, grob zuschneiden; der Server baut das PDF, der Worker richtet gerade und erkennt den Text (146). Das PDF der App ist gegen Poppler und OCRmyPDF geprüft. 174 Tests grün.

**M3.5 Scan-Ordner ✅ (28.11.2026):** Freigabe als `/scan`, Haushalt im Ordner, privat im Unterordner mit der E-Mail-Adresse, Übernahme nach Ende des Schreibens, danach gelöscht (147). 177 Tests grün.

**M3.6 Mail-Eingang ✅ (28.11.2026):** eigenes IMAP-Postfach, Absender nur Mitglieder mit bestandener SPF/DKIM/DMARC-Prüfung, Anhänge aus der Positivliste, Makros markiert, Mailtext als Dokument, keine doppelten Mails (148–151). 192 Tests grün, dazu 4 gegen den echten Worker. **Damit ist M3 fertig.** Nicht geprüft: ein echter IMAP-Server (in den Tests eine Attrappe).

1. Container `mydocs-worker`, Auftragstabelle, OCRmyPDF zu PDF/A, LibreOffice-Vorschau, Vorschaubilder
2. Volltextsuche über den Dokumenttext (`german`, `pg_trgm`)
3. Posteingang mit Zuordnung, Dokumentart und Datum
4. Kamera-Upload (Seiten sammeln, sortieren, zuschneiden)
5. Scan-Ordner auf der Synology
6. IMAP mit Absenderprüfung, Positivliste, Markierung von Makros

**Abnahme:** Ein Brief, der per Kamera, Scanner und Mail hereinkommt, ist jedes Mal durchsuchbar und zugeordnet.

## M4 – Desktop-Client (17, 36–38, 41)

**M4.1 Anmeldung und Host ✅ (28.11.2026):** Systembrowser mit PKCE und Loopback, Gerätetokens mit Refresh im Schlüsselspeicher, Seiten „Geräte“ und „Gerät anmelden“, Photino-Host mit `MyDocs.UI` (152–155). 212 Tests grün; der Client startet auf macOS und zeigt die Oberfläche, der ganze Ablauf mit Browser ist nur in den Tests geprüft.

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
