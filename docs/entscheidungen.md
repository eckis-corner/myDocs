# Entscheidungsprotokoll myDocs

Fortlaufend nummeriert. Jede Rückfrage behält ihre Nummer, die Antwort steht dahinter. Nummern werden nie neu vergeben.

## Runde 1 (27.11.2026)

### Abgrenzung und Grundlagen

| Nr. | Thema | Entscheidung |
| ---: | --- | --- |
| 1 | Rolle der Apps | **myDocs ist der Alltag, myLife der Notfall.** myDocs führt Objekte und Dokumente und steuert die Bearbeitung. myLife erhält daraus, was im Ernstfall gebraucht wird. |
| 2 | Sicherheitsmodell | **Vertrauenswürdiger Server, Verschlüsselung at rest.** Kein Zero-Knowledge. Der Server darf Inhalte lesen, damit OCR, Volltextsuche, Vorschau und Versionierung möglich sind. Später optional: einzelne Objekte zusätzlich clientseitig verschlüsselt. |
| 3 | Was nicht hineingehört | Wie bei myLife: keine Passwörter, PINs, TANs, vollständigen Kartennummern oder Seed-Phrasen. Prüfungen bei der Eingabe aus `MyCommon.Catalog`. |
| 4 | Nutzer | Eine Instanz pro Familie. **Haushalt als gemeinsamer Bestand** plus **private Objekte je Person**. Jede Person hat ein eigenes Login. **Nur der Administrator legt Benutzer an**, keine Registrierung, keine Einladungen durch Nutzer. |
| 5 | Personen ohne Konto | Kinder und andere Beteiligte können als **Personen-Stammdaten ohne Login** geführt werden (z. B. versicherte Person). |

### Objekte

| Nr. | Thema | Entscheidung |
| ---: | --- | --- |
| 6 | Objekttypen | **Gemeinsame Bibliothek** `MyCommon.Catalog` (NuGet), aus `MyLife.Shared` herausgelöst. Ein myDocs-Objekt vom Typ `insurance` ist ein myLife-Objekt vom Typ `insurance`. Regel aus myLife gilt weiter: Schlüssel und Auswahlwerte nur ergänzen, nie umbenennen. |
| 7 | Zusatztypen | Typen nur für myDocs tragen `MyLifeRelevant = false` und werden nie übertragen. Arbeitsstand: Rechnung/Beleg, Garantie/Gewährleistung, Steuerjahr, Korrespondenz, Gesundheitsunterlagen, Schule/Ausbildung. **Felder offen.** |
| 8 | Status | **Zwei Achsen:** Lebenszyklus am Objekt (Zustände je Typ aus dem Katalog) und Bearbeitung über **Vorgänge**. Keine frei konfigurierbaren Workflows. |
| 9 | Lebenszyklus-Muster | Drei Muster, jeder Typ wählt eines: **Vertrag** (in Abschluss → aktiv → gekündigt → beendet → archiviert), **Gültigkeit** (gültig → läuft ab → abgelaufen → ersetzt → archiviert), **Bestand** (vorhanden → abgegeben → archiviert). „läuft ab“ und „abgelaufen“ setzt das System aus dem Ablaufdatum. |
| 10 | Folgevorgänge | Fest im Katalog, z. B. gekündigt → „Kündigungsbestätigung ablegen“ (14 Tage); beendet → „Letzte Abrechnung prüfen“, „Lastschrift prüfen“; abgelaufen → „Ersatz anlegen“; ersetzt → Wiedervorlage geht auf den Nachfolger über. |
| 11 | Vorgang | Titel, Art (Prüfung, Frist, Aufgabe, Posteingang, Aufräumen, myLife-Abgleich), Objekte (0..n), zuständige Person, Fälligkeit, Priorität (normal/wichtig), Status (offen, in Arbeit, wartet auf Dritte, erledigt, verworfen), Ergebnis, Notiz, Dokumente. Manuelle Vorgänge erlaubt. **„wartet auf Dritte“ verlangt ein Wiedervorlagedatum.** |
| 12 | Zuständigkeit | Jedes Objekt hat eine zuständige Person, bei Haushaltsobjekten Pflicht. |

### Wiedervorlage

| Nr. | Thema | Entscheidung |
| ---: | --- | --- |
| 13 | Herkunft des Datums | **Aus Regeln berechnet**, von Hand überschreibbar: Prüfintervall je Typ, Fristfelder (letzter Kündigungstermin aus Hauptfälligkeit und Kündigungsfrist), Ablaufdatum. Regeln als `ReviewRule` im Katalog, je Objekt eigene Regeln möglich. **Intervalle je Typ offen.** |
| 14 | Eskalation | Dashboard oben; Mail/ntfy am Fälligkeitstag, nach 7 und nach 14 Tagen an weitere Haushaltsmitglieder; danach Markierung **„überfällig“**, die mit nach myLife wandert („Stand ungeprüft seit …“). |
| 15 | Abschluss | Eine Prüfung schließt nur mit **Ergebnis**: unverändert, geändert, gekündigt, ersetzt durch …, plus optionale Notiz. Die nächste Wiedervorlage berechnet das System. |
| 16 | Zurückstellen | Nur mit neuem Datum (höchstens 30 Tage) und Begründung, sichtbar in der Historie. |

### Dokumente

| Nr. | Thema | Entscheidung |
| ---: | --- | --- |
| 17 | Office bearbeiten | **Check-out/Check-in über den Desktop-Client**: herunterladen, mit der Standard-App öffnen, Änderungen beobachten, als neue Version hochladen, Sperre für andere. WebDAV ist Ausbaustufe, Collabora/OnlyOffice vorerst nicht. |
| 18 | Mobil | Office-Dokumente **nur ansehen** (PDF-Vorschau vom Server). |
| 19 | Eingangskanäle | Upload und Drag & Drop, Eingangsordner am Desktop, Scan-Ordner auf der Synology, IMAP-Postfach, **Kamera-Upload (wird zu PDF)**. |
| 20 | Posteingang | Alles aus Kanälen außer dem direkten Upload am Objekt landet im Posteingang: zuordnen (1..n Objekte), Dokumentart, Dokumentdatum, optional Vorgang erzeugen. Liegt ein Eintrag länger als X Tage, wird er fällig. Später: Zuordnungsvorschläge aus OCR (Policen-, Kunden-, Vertragsnummern, IBAN gegen Objektfelder). |
| 21 | Zuordnung | Dokument ↔ Objekt **n:m**. Dokumente ohne Objekt über den Typ „Korrespondenz“. |
| 22 | Speicherung | Dateisystem, **adressiert über SHA-256** (Duplikaterkennung). **AES-256-GCM mit Schlüssel je Datei**, verpackt mit dem Server-Hauptschlüssel außerhalb der Datenbank. Metadaten in PostgreSQL. |
| 23 | Kamera | Browser sammelt, sortiert und schneidet grob zu. Entzerren, OCR und PDF-Erzeugung macht der Server, einheitlich für alle Kanäle. |
| 24 | Verarbeitung | Eigener Container **`mydocs-worker`** mit LibreOffice headless, OCRmyPDF und Tesseract (de, en). Warteschlange, eine Aufgabe gleichzeitig: Typ erkennen → Office zu PDF-Vorschau → OCR zu PDF/A → Text indizieren → Vorschaubild → Zuordnungsvorschläge. **Original bleibt immer erhalten.** |
| 25 | Suche | **PostgreSQL-Volltextsuche** (`german`) plus `pg_trgm`. Über Objektfelder, Dokumenttext, Notizen und Vorgänge. Filter: Bereich, Typ, Person, Status, Zeitraum, Archiv. |
| 26 | Suche und Rechte | Rechte werden **in der Abfrage** geprüft. Fremde private Treffer tauchen nie auf, auch nicht als Anzahl. |
| 27 | Versionen | Objekte: Änderungshistorie je Feld (alt → neu, wer, wann). Dokumente: alle Versionen unveränderlich mit Hash. Später optional eine Regel zum Ausdünnen. |
| 28 | Aufbewahrung | Je **Dokumentart** eine vorgeschlagene Dauer und ein Bezugspunkt (Dokumentdatum, Ende des Objekts, Steuerjahr). Nach Ablauf entsteht ein **Vorgang „Aufräumen“**. **Nie automatisch löschen.** Werte je Dokumentart offen. |
| 29 | Archiv | Archivierte Objekte verschwinden aus Listen und Wiedervorlagen, bleiben über die Suche (Filter „Archiv“) erreichbar und gehen beim Abgleich als *beendet* an myLife. |
| 30 | Löschen | Papierkorb 30 Tage. Endgültig mit allen Versionen, OCR-Text, Vorschau und Index. Ein Blob wird nur entfernt, wenn kein anderes Dokument denselben Hash nutzt. Audit-Eintrag bleibt ohne Inhalt. |

### Rechte

| Nr. | Thema | Entscheidung |
| ---: | --- | --- |
| 31 | Administrator | **Sieht alles**, auch private Objekte anderer. Aber nur im bewussten Modus **„Admin-Einsicht“**: Im normalen Dashboard und in der Suche tauchen fremde private Objekte nicht auf. Jeder Zugriff wird protokolliert und **dem Eigentümer in seinem Audit-Log angezeigt**. Hinweis beim ersten privaten Objekt und auf der Datenschutzseite. |
| 32 | Rollen im Haushalt | *Mitglied* (lesen und schreiben) und *Gast* (nur lesen). Keine Rechte je Bereich. |
| 33 | Private Objekte teilen | Je Objekt an bestimmte Personen, Recht *lesen* oder *bearbeiten*. Dokumente erben die Freigabe. |
| 34 | Besitzwechsel | Privat ↔ Haushalt möglich, in der Historie protokolliert. Haushalt → privat nur bei eigenen Objekten. |
| 35 | Audit-Log | Ansehen, Ändern, Herunterladen, Freigeben. Jede Person sieht die Einträge zu ihren Objekten. |

### Technik

| Nr. | Thema | Entscheidung |
| ---: | --- | --- |
| 36 | Desktop | **Voll-Client** mit **Photino.Blazor** (Windows, macOS, Linux). Check-out, File-Watcher, Eingangsordner und Tray laufen im Client-Prozess. |
| 37 | UI-Struktur | Die gesamte Oberfläche liegt in einer Razor Class Library `MyDocs.UI` und arbeitet **nur über die REST-API** (`MyDocs.ApiClient`), im Browser wie im Desktop. |
| 38 | Offline | **Nur online.** Ausgecheckte Dateien lassen sich offline weiter bearbeiten und werden später eingecheckt. Offline-Lesen ist Ausbaustufe. |
| 39 | Web-Render-Modus | **Interactive WebAssembly**, wie myLife. |
| 40 | Anmeldung Browser | ASP.NET Core Identity, Cookie (`HttpOnly`, `Secure`, `SameSite=Strict`), TOTP oder Passkey. |
| 41 | Anmeldung Desktop | Einmalig über den Systembrowser (Authorization Code mit PKCE, Loopback-Umleitung), dann **Gerätetoken** mit Refresh im Schlüsselspeicher des Betriebssystems (Credential Manager, Keychain, libsecret). Geräte sind in der Web-App widerrufbar. Kein eigener Identity-Server. |
| 42 | Datenbank | **Eigener PostgreSQL-17-Container**, getrennt von myLife. |
| 43 | Container | `mydocs-app` (512 MB), `mydocs-worker` (1–1,5 GB), `mydocs-db` (512 MB). Mit myLife zusammen ca. 3 GB auf der DS1520+ mit 8 GB RAM. Worker läuft dauerhaft. |
| 44 | Erreichbarkeit | `https://mydocs.eckis-corner.de` über den vorhandenen Nginx Proxy Manager, gleich wie myLife. Pflicht: 2FA, Rate-Limiting, Sperre nach Fehlversuchen, CSP. |
| 45 | Sicherung | Muster von myLife: tägliche Generationen (DB-Schnappschuss, Data-Protection-Schlüssel, Dateien), verschlüsselt mit `MYDOCS_BACKUP_KEY`, Ablage `/data/backup`, Abholung durch das Synology-Backup, Befehl `restore`. |
| 46 | Server-Hauptschlüssel | Gehört in die Sicherung **und auf Papier** (24 Wörter). Wird in myLife als Objekt „myDocs-Wiederherstellung“ mit Ablageort geführt. |
| 47 | Benachrichtigung | Mail (SMTP) und optional ntfy, im Desktop zusätzlich Benachrichtigungen des Systems. Je Person eine **tägliche Zusammenfassung** zur wählbaren Uhrzeit, sofort nur Eskalationsstufe 3. **SMTP getrennt von myLife.** |
| 48 | Sprache | Deutsch und Englisch über `.resx` (Grundbestand aus `MyCommon`). OCR und Suche vorrangig Deutsch, Englisch als zweite Tesseract-Sprache. |
| 49 | Mail-Eingang | Nur von hinterlegten Absenderadressen der Haushaltsmitglieder, geprüft über SPF/DKIM (`Authentication-Results`). Landet privat im Posteingang des Absenders. Anhänge nur aus einer Positivliste (PDF, Bilder, Office). Office-Makros werden markiert, nicht entfernt. Mailtext auf Wunsch als PDF. **Kein ClamAV** vorerst (RAM). |
| 50 | Kommentare | Einfacher Verlauf je Vorgang und je Objekt, ohne @-Erwähnungen. |

### Gemeinsamer Code mit myLife

| Nr. | Thema | Entscheidung |
| ---: | --- | --- |
| 51 | Pakete | **`MyCommon.Catalog`** (Schema, Katalog, Auswahllisten, Feldprüfungen, Ressourcen) und **`MyCommon.Ui`** (Theme, Logo-Komponente, Sprach- und Benutzermenü, Grundlayout, ObjectForm und ObjectDatasheet aus dem Schema, entkoppelt von der myLife-Krypto). |
| 52 | Repository | **`eckis-corner/my-common`**, Pakete über GitHub Packages. Lokal per bedingter `ProjectReference` in `Directory.Build.props`, wenn `../my-common` existiert. |
| 53 | Reihenfolge | **Zuerst wird myLife umgebaut** (Pakete herauslösen, Tests grün, Release myLife 1.2). |
| 54 | Lizenz | **AGPL-3.0** für myDocs und my-common. |
| 55 | Marke | Gemeinsames Theme, **eigenes Logo und eigene Akzentfarbe** je App. |

### Brücke zu myLife

| Nr. | Thema | Entscheidung |
| ---: | --- | --- |
| 56 | Richtung | **myLife zieht.** Im entsperrten myLife-Browser holt die Seite „Abgleich mit myDocs“ über ein **lesendes Token** die markierten Objekte im Klartext über TLS, zeigt die Unterschiede, verschlüsselt und speichert nach Bestätigung. Die Kryptografie bleibt allein in myLife. Nötig: `connect-src` in der CSP von myLife, CORS in myDocs. |
| 57 | Umfang | Markierung je Objekt „In myLife führen“, vorbelegt bei `MyLifeRelevant = true`. Übertragen werden Katalogfelder, Lebenszyklus und zuletzt geprüft/überfällig. Vorgänge, Historie und Notizen bleiben in myDocs. |
| 58 | Dokumente | Nur ausdrücklich markierte, als **feste Kopie** (keine Versionen), Grenze von myLife: 25 MB. |
| 59 | Konflikte | Einbahnstraße myDocs → myLife. Felder aus myDocs sind in myLife nach der Verknüpfung **schreibgeschützt** („gepflegt in myDocs“). |
| 60 | Anstoß | Der Abgleich passiert, wenn der Nutzer myLife öffnet. myDocs merkt sich den letzten Abgleich je Person. Nach 30 Tagen mit offenen Änderungen entsteht der **Vorgang „myLife abgleichen“**. |
| 61 | Erstbefüllung | **Kein Import aus myLife.** myLife hat noch keine echten Daten. Die Einbahnstraße gilt ohne Ausnahme. |
| 62 | Zahlungen | Die vollständige Übersicht führt myDocs, mit Haushaltssicht und dem Filter „gekündigt zum …“. myLife behält seine Übersicht unverändert. Logik über `PaymentDefinition` im Katalog. |
| 63 | Ablageorte | **myDocs führt die Ablageorte**, sie wandern wie andere Objekte nach myLife. (Korrigiert die Ausnahme aus der ersten Fassung von 59.) |
| 64 | Nur in myLife | Wünsche für die Hinterbliebenen, Briefe, Bestattung, Organspende, Checkliste, Notfallzugang des Passwortmanagers, Vertrauenspersonen und Notfallzugang. |

### Oberfläche und Vorgehen

| Nr. | Thema | Entscheidung |
| ---: | --- | --- |
| 65 | Dashboard | 1. Überfällig und diese Woche fällig; 2. Posteingang; 3. Fristen der nächsten 90 Tage; 4. Zahlungen pro Monat und Veränderung zum Vorjahr; 5. Zustand des Bestands (unvollständig, ungeprüft > 12 Monate, myLife-Abgleich offen). Umschalter „Meins“ / „Haushalt“. |
| 66 | Meilensteine | M0 my-common → M1 Grundgerüst → M2 Arbeiten am Bestand → M3 Posteingang und Worker → M4 Desktop-Client → M5 Brücke zu myLife → M6 Komfort. Desktop vor der Brücke. |
| 67 | Echte Daten | Ab M2, nach eigener Abnahme auf der Synology inklusive Test der Rücksicherung. |
| 68 | Dokumentation | Struktur wie myLife; dieses Protokoll ist die Grundlage. |

## Runde 2 (27.11.2026, Start von M0)

| Nr. | Thema | Entscheidung |
| ---: | --- | --- |
| 69 | Paketnamen | **`MyCommon.Catalog`** und **`MyCommon.Ui`** bestätigt (zu 51). |
| 70 | Aufteilung M0 | **M0.1** Katalog, **M0.2** Oberfläche. Formular und Datenblatt hängen in myLife eng an `VaultService` und brauchen eigene Abstraktionen. |
| 71 | Katalogtexte | Quelle ist `catalog-texts.json` in MyCommon.Catalog, abrufbar über `CatalogTexts`. myLife übernimmt die Texte mit `tools/resources.py` in seine eigenen Ressourcen (unverändert, damit `mylife-decrypt` und alle Seiten weiterlaufen). |
| 72 | Prüfung | `FieldValues.ValidateFields` und `IsVisible` arbeiten über `IFieldValues`. Die gemeinsamen Angaben prüft jede App selbst (myLife: `VaultValidation`). |
| 73 | GitHub Packages | NuGet verlangt dort auch bei öffentlichen Paketen eine Anmeldung. Apps nutzen `nuget.config` mit Zuordnung `MyCommon.*`; CI und Docker melden sich über `NuGetPackageSourceCredentials_eckiscorner` an. Das Paket muss den Repos myLife und myDocs in den Paket-Einstellungen Lesezugriff für Actions geben. |

## Offen

| Thema | Was fehlt |
| --- | --- |
| Zusatztypen (7) | Felder, Bereich und Lebenszyklus-Muster je Typ |
| Prüfregeln (13) | Standard-Prüfintervall je Typ, Vorlauf bei Fristen |
| Aufbewahrung (28) | Dauer und Bezugspunkt je Dokumentart |
| Posteingang (20) | Wert für „X Tage“, bis ein Eintrag fällig wird |
| Bereiche | Ob myDocs die 15 Bereiche von myLife übernimmt oder eigene Bereiche für die Zusatztypen bekommt |
| Personen-Stammdaten (5) | Verhältnis zum myLife-Typ „Kontakt“ und zu Benutzern |
| Größengrenze | Maximale Dateigröße in myDocs (myLife: 25 MB) |
