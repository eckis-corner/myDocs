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
| 74 | Umfang MyCommon.Ui | Farbschema mit Akzentfarbe je App (`EckisTheme`, `AppAccent`), Schrift Titillium Web, `CatalogText` (Texte, Datenblatt-Zeilen der Felder) und `CatalogFields` (Formular der Felder eines Typs, Ablageort-Felder über eine Vorlage der App). Die allgemeinen Texte der Feldtypen (Ja/Nein, Verweise, Fehler) gehören zum Katalog. |
| 75 | Bleibt in den Apps | Logo, Sprach- und Benutzermenü, Layout und das CSS. Sie hängen in myLife an Sitzung, Tresor und Kulturdienst. Sie werden herausgelöst, sobald myDocs in M1 eigene Gegenstücke hat und klar ist, was beide wirklich teilen. Die Akzentfarbe von myDocs wird in M1 festgelegt. |

## Runde 3 (27.11.2026, Start von M1)

| Nr. | Thema | Entscheidung |
| ---: | --- | --- |
| 76 | Bereiche | myDocs übernimmt die **Bereiche des Katalogs** für Navigation und Übersicht. Die Zusatztypen (7) bekommen neue Bereiche ab 15 im Katalog. Die Bereiche nur für den Notfall (11 Persönliche Wünsche, 12 Checkliste) zeigt myDocs nicht. |
| 77 | Personen | Eigene Tabelle **Personen** für Haushaltsmitglieder und Kinder, mit oder ohne Login; sie tragen Rechte und Zuständigkeit. Der Katalogtyp **Kontakt** bleibt für Außenstehende. Ob Felder wie Versicherungsnehmer später auf Personen verweisen, klärt M2. (Zu 5.) |
| 78 | Dateigröße | **100 MB** je Datei, vom Administrator änderbar. |
| 79 | Akzentfarbe und Logo | **Petrol #1F9E8F** (dunkle Schrift darauf); als Schriftfarbe im hellen Modus #0F6B60. Logo im Stil von myLife: Quadrat mit zwei Blättern, Zeilen in Petrol, Schriftzug „my“ im Akzent und „Docs“ in der Textfarbe. |

## Runde 4 (27.11.2026, M1.2 Einrichtung und Anmeldung)

| Nr. | Thema | Entscheidung |
| ---: | --- | --- |
| 80 | Einrichtung | Wie myLife mit **Einrichtungscode** aus dem Container-Protokoll, neu bei jedem Start, bis ein Administrator mit bestätigtem zweitem Faktor existiert. Drei Schritte: Code und Konto, Authenticator-App (QR-Code im Browser), **10 Wiederherstellungscodes**. Ein abgebrochener Versuch ohne zweiten Faktor wird beim nächsten ersetzt. |
| 81 | Zweiter Faktor | **Pflicht für jedes Konto.** Ein Konto ohne zweiten Faktor kann sich nicht anmelden. TOTP nach RFC 6238 (±30 Sekunden), **jeder Code gilt nur einmal** (eigener Token-Provider statt des Standards von Identity). Statt Code geht ein Wiederherstellungscode, jeder einmal. Passkeys folgen mit M1.2b. |
| 82 | Geheimnisse | TOTP-Schlüssel und Wiederherstellungscodes liegen **mit ASP.NET Data Protection verschlüsselt** in der Datenbank (Schlüssel in `data/keys`). Passwörter hasht Identity (PBKDF2). |
| 83 | Sitzung | Cookie `mydocs.session` (HttpOnly, SameSite=Strict, Secure), 30 Minuten gleitend. Zwischen Passwort und zweitem Faktor das Cookie `mydocs.2fa`, 5 Minuten. Sperre nach 5 Fehlversuchen für 15 Minuten, Rate-Limit 30 Anfragen je Minute und IP an Einrichtung und Anmeldung. Unbekannte Adressen antworten gleich und gleich langsam wie falsche Passwörter. |
| 84 | Datenbank-Namen | snake_case über EFCore.NamingConventions; die Tabellen von Identity heißen `users`, `roles`, `user_tokens` usw. Identity-Schema Version 3 (mit `user_passkeys`). |

## Runde 5 (27.11.2026, M1.3 Benutzer, Personen, Haushalt)

| Nr. | Thema | Entscheidung |
| ---: | --- | --- |
| 85 | Reihenfolge | M1.3 vor den Passkeys (M1.2b); für den Anfang reicht TOTP. |
| 86 | Neues Konto | Der Administrator gibt Name, E-Mail-Adresse und Rolle ein. myDocs erzeugt einen **Aktivierungslink** `/activate#<Token>`: 256 Bit, nur als SHA-256 gespeichert, **einmal gültig, 7 Tage**. Das Token steht hinter `#` und landet damit in keinem Server-Protokoll. Bis zum Mailversand gibt der Administrator den Link selbst weiter. Über den Link legt die Person Passwort, Authenticator-App und Wiederherstellungscodes fest. |
| 87 | Rollen | Identity-Rollen `member` und `guest` für den Haushalt (32), dazu `admin` als Zusatzrecht. **Administratoren sind immer Mitglied.** Lesen dürfen alle Angemeldeten, ändern nur Mitglieder (Policy `member`). |
| 88 | Verwaltung | Der Administrator ändert Name, Rolle und Admin-Recht, **sperrt und entsperrt** und **setzt den Zugang zurück** (Passwort, zweiter Faktor und Wiederherstellungscodes entfallen, neuer Aktivierungslink). Das eigene Konto lässt sich weder sperren, zurücksetzen noch herabstufen; der letzte aktive Administrator bleibt immer erhalten. Neue Rechte, Sperre und Zurücksetzen beenden laufende Sitzungen nach spätestens einer Minute (Security Stamp). Löschen von Konten folgt, wenn es Objekte gibt. |
| 89 | Personen | Tabelle `persons` (Name, Geburtsdatum, Notiz, optional Konto). Jedes Konto ist genau eine Person, ihr Name ist der des Kontos. Mitglieder legen Personen ohne Konto an, ändern und entfernen sie (Papierkorb-Kennzeichen). Personen mit Konto lassen sich nicht entfernen. |
| 90 | Aktualisierung | Die Migration `Household` macht bestehende Administratoren zu Mitgliedern und legt für jedes Konto eine Person an. |

## Runde 6 (27.11.2026, M1.4 Objekte)

| Nr. | Thema | Entscheidung |
| ---: | --- | --- |
| 91 | Inhalt eines Objekts | Typ aus dem Katalog, Titel, Felder als `jsonb` in kanonischer Form, Ablageorte (Verweis und Name wie myLife), Notizen, Ablaufdatum. Nicht in myDocs: die Typen der Bereiche 11 und 12 und das eigene Datenblatt `profile` (Personen führt myDocs selbst). Verknüpfungen entstehen über die Verweisfelder des Katalogs und werden im Datenblatt in beide Richtungen gezeigt; freie Verknüpfungen und Zusatzfelder kommen bei Bedarf später. |
| 92 | Rechte | Geprüft in jeder Abfrage (26). Haushaltsobjekte sehen alle Angemeldeten, ändern nur Mitglieder. Private Objekte sieht und ändert der Eigentümer, dazu wer eine Freigabe hat (lesen oder bearbeiten). Fremde private Objekte erscheinen nirgends, auch nicht in Listen, Historie oder Papierkorb. Anlegen dürfen nur Mitglieder, privat nur für sich selbst. |
| 93 | Eigentum | Privat → Haushalt durch den Eigentümer. Haushalt → privat nur durch die zuständige Person und nur für sich selbst (zu 34). Wer nur eine Freigabe hat, ändert das Eigentum nicht und löscht nicht. Im Haushalt gibt es keine Freigaben; sie entfallen beim Wechsel. |
| 94 | Gleichzeitige Änderungen | Version über `xmin` von PostgreSQL. Wer auf einem veralteten Stand speichert, bekommt einen Konflikt (409) statt die Änderung des anderen zu überschreiben. |
| 95 | Historie und Papierkorb | Jede Änderung von Titel, Feldern, Notizen, Ablaufdatum, Eigentum und Zuständigkeit landet mit Wer und Wann in `object_history`, ebenso Löschen und Wiederherstellen. Gelöschte Objekte liegen im Papierkorb, bis jemand mit Verwaltungsrecht sie wiederherstellt; das endgültige Leeren nach 30 Tagen kommt mit M1.7. |
| 96 | Admin-Einsicht | Wandert nach **M1.7**, weil jeder Zugriff im Audit-Log des Eigentümers erscheinen soll (31) und das Audit-Log mit M1.7 kommt. Bis dahin sieht auch der Administrator fremde private Objekte nicht. |

## Runde 7 (27.11.2026, M1.5 Dokumente)

| Nr. | Thema | Entscheidung |
| ---: | --- | --- |
| 97 | Server-Hauptschlüssel | 32 Byte in `data/keys/master.key` (Rechte 600), angelegt beim ersten Start. Daraus per HKDF ein Schlüssel zum Verpacken der Dateischlüssel und einer für die Dateinamen. Ausdruck als 24 Wörter und Sicherung folgen mit M1.7 (46). |
| 98 | Verschlüsselung | Jede Datei hat einen eigenen Zufallsschlüssel, verpackt mit dem Hauptschlüssel (AES-256-GCM). Inhalt in Blöcken von 1 MiB mit AES-256-GCM, Nonce aus der Blocknummer, letzter Block in der AAD markiert (Format `MDB1`). Klartext liegt nie auf der Platte: Uploads werden beim Lesen verschlüsselt, Downloads beim Senden entschlüsselt. |
| 99 | Dateinamen und Duplikate | Dateiname ist der HMAC-SHA-256 des Klartexts (`files/ab/ab…`). Gleicher Inhalt liegt nur einmal da (Referenzzähler); ohne Hauptschlüssel lässt sich nicht prüfen, ob eine bekannte Datei vorhanden ist (ändert 22: HMAC statt reinem SHA-256). |
| 100 | Dokumente und Rechte | Dokument mit Titel, Datum, Notiz und unveränderlichen Versionen. Zuordnung zu einem oder mehreren Objekten, mindestens einem. Rechte erbt das Dokument von seinen Objekten (33): Wer eines sieht, sieht und lädt herunter; wer eines bearbeiten darf, lädt Versionen hoch, ändert die Angaben, ordnet zu und löscht. Zuordnen nur zu Objekten, die man selbst bearbeiten darf. |
| 101 | Vorschau | Direkt im Browser nur PDF, PNG, JPEG, GIF und WebP. Alles andere, vor allem SVG und HTML, nur als Download mit `application/octet-stream`. Office-Vorschau kommt mit M3 (Worker). Grenze 100 MB je Datei (78) gilt im Browser und auf dem Server. |

## Runde 8 (27.11.2026, M1.6 Suche)

| Nr. | Thema | Entscheidung |
| ---: | --- | --- |
| 102 | Suchtext | Der Server berechnet beim Speichern je Objekt und Dokument einen Suchtext: Titel, Art (de und en), alle Feldwerte, Auswahlwerte mit deutscher und englischer Bezeichnung, IBANs auch gruppiert, Zahlen auch mit Komma, Notizen; bei Dokumenten Titel, Notiz und alle Dateinamen. Den Inhalt der Dokumente durchsucht erst die OCR in M3. Fehlende Suchtexte berechnet der Server beim Start nach. |
| 103 | Verfahren | PostgreSQL-Volltext mit Konfiguration `german` (Wortstämme, gespeicherte Spalte `search_vector`, GIN-Index), dazu `ILIKE` für Teilwörter und `word_similarity` aus `pg_trgm` (> 0,5) für Tippfehler, beide über einen GIN-Trigramm-Index. Rangfolge aus `ts_rank` plus Ähnlichkeit, höchstens 50 Treffer. Mindestens zwei Zeichen. |
| 104 | Rechte und Oberfläche | Die Rechte stecken in der Abfrage (26); Dokumente nur über sichtbare, nicht gelöschte Objekte. Suchfeld in der Kopfzeile, Seite „Suche“ mit Treffern und der Zeile, in der der Begriff vorkommt. Ein Dokument-Treffer führt zu seinem Objekt. |

## Runde 9 (27.11.2026, M1.7a Protokoll, Admin-Einsicht, Papierkorb)

| Nr. | Thema | Entscheidung |
| ---: | --- | --- |
| 105 | Aufteilung M1.7 | **M1.7a** Protokoll, Admin-Einsicht, Papierkorb; **M1.7b** Sicherung, `restore`, Hauptschlüssel als Ausdruck, Installationsanleitung. |
| 106 | Was protokolliert wird | Anmelden, fehlgeschlagene Anmeldung (Passwort, Code, Sperre), Abmelden, Einrichtung, Aktivierung, alle Aktionen der Benutzerverwaltung; Anlegen, Ändern (mit den geänderten Feldern, ohne Werte), Löschen, Wiederherstellen und Freigeben von Einträgen; Hochladen, neue Version, Öffnen, Löschen und Wiederherstellen von Dokumenten; Leeren des Papierkorbs. **Ansehen** nur, wenn jemand einen fremden privaten Eintrag öffnet (Freigabe oder Admin-Einsicht); jeden Blick auf Haushaltseinträge zu speichern, machte das Protokoll unbrauchbar. Titel werden mitgeschrieben, Inhalte nicht. IP gekürzt (IPv4 ohne letztes Byte, IPv6 auf /48). |
| 107 | Wer was sieht | Jede Person: eigene Aktionen und alles, was andere mit ihren privaten Einträgen und Dokumenten tun, Seite „Protokoll“ im Benutzermenü. Administrator: gesamtes Protokoll unter Verwaltung. |
| 108 | Admin-Einsicht | Umsetzung von 31: Schalter im Benutzermenü mit Rückfrage, deutliches Band „Admin-Einsicht aktiv“, gilt bis zum Neuladen oder Abmelden. Der Client schickt dann `X-MyDocs-Admin-View: 1`; ohne Rolle Administrator ist der Header wirkungslos. In der Admin-Einsicht sieht der Administrator alle Einträge, Dokumente und Suchtreffer, **ändert aber nichts**. Jeder Zugriff auf einen fremden privaten Eintrag, auch in Listen und Suche, erscheint als „Admin-Einsicht“ im Protokoll des Eigentümers. |
| 109 | Papierkorb leeren | Hintergrunddienst einmal täglich: Einträge und Dokumente, die länger als 30 Tage gelöscht sind, sowie Dokumente ohne Eintrag verschwinden endgültig, mit Versionen, Freigaben und Historie. Die verschlüsselte Datei wird erst gelöscht, wenn keine Version mehr auf sie verweist. Die Einträge im Protokoll bleiben. |

## Runde 10 (27.11.2026, M1.7b Sicherung und Betrieb)

| Nr. | Thema | Entscheidung |
| ---: | --- | --- |
| 110 | Sicherung | Übernommen von myLife: täglich zur Uhrzeit (Standard 3 Uhr) eine Sicherung `mydocs-<Datum>-<Uhrzeit>.mdbak` in `data/app/backup`, Format `MYDOCSBK` (AES-256-GCM in 1-MiB-Blöcken, Schlüssel per HKDF aus `MYDOCS_BACKUP_KEY` und einem Salz je Sicherung). Inhalt: Datenbank als Schnappschuss (`COPY … FORMAT binary` in REPEATABLE READ), der Ordner `keys` mit Data Protection und Hauptschlüssel, die verschlüsselten Dateien. Generationen 7 Tage, 4 Wochen, 6 Monate. Einstellungen bis zur Einstellungsseite über Umgebungsvariablen. Seite „Sicherung“ mit Status und „Jetzt sichern“. |
| 111 | Rücksicherung | `docker compose run --rm app restore <Datei> --confirm` bei gestoppter App: Schema neu, Migrationen bis zum Stand der Sicherung, Tabellen leeren (Migrationen legen auch Daten an), Daten per `COPY`, Dateien und Schlüssel zurück, danach die übrigen Migrationen. Der Kontext für die Rücksicherung entsteht mit denselben Einstellungen wie in der App (`MyDocsDbContextFactory`). |
| 112 | Hauptschlüssel als Ausdruck | Seite „Sicherung“: Der Administrator gibt einen frischen Code seiner Authenticator-App ein und sieht den Hauptschlüssel als **24 Wörter** (BIP-39, Wortliste wie myLife) zum Drucken; die Anzeige steht im Protokoll. Zurückschreiben: `master-key restore "<24 Wörter>" --confirm`. |
| 113 | Betrieb | Installationsanleitung `docs/betrieb/installation.md` (Port 8090, `client_max_body_size 110m`, eigener Projektordner neben myLife) und Abnahme-Checkliste `docs/betrieb/abnahme.md`. |

## Runde 11 (27.11.2026, M1.2b Passkeys)

| Nr. | Thema | Entscheidung |
| ---: | --- | --- |
| 114 | Passkeys | Mit den Passkeys von ASP.NET Core Identity 10 (kein Fido2NetLib). Ein Passkey **ersetzt nach dem Passwort den Code** der Authenticator-App (wie myLife); passwortloses Anmelden gibt es nicht. Registrieren und Entfernen unter Benutzermenü → Sicherheit; dort steht auch, wie viele Wiederherstellungscodes übrig sind. Der Ursprung wird gegen den `Origin`-Header geprüft, den Browser bei POST mitschicken. |
| 115 | Schritt nach dem Passwort | Identity legt den Zustand der Passkey-Zeremonie im Zwischen-Cookie ab und verliert dabei den Benutzer aus dem Passwort-Schritt. myDocs hält ihn in einem eigenen, signierten Cookie (`mydocs.passkey-step`, Data Protection, 5 Minuten, nur für `/api/auth`) fest; der Passkey muss zu genau diesem Konto gehören. Ein fehlgeschlagener Passkey zählt als Fehlversuch für die Sperre. |

## Runde 12 (27.11.2026, M2.1 Lebenszyklus, Vorgänge, Kommentare)

| Nr. | Thema | Entscheidung |
| ---: | --- | --- |
| 116 | Aufteilung M2 | **M2.1** Lebenszyklus, Vorgänge, Kommentare; **M2.2** Prüfregeln, Wiedervorlage, Eskalation; **M2.3** Dashboard; **M2.4** Mail, ntfy, tägliche Zusammenfassung und Einstellungsseite. |
| 117 | Ort der Regeln | Lebenszyklus-Muster, Folgevorgänge und Prüfregeln stehen vorerst in myDocs (`Lifecycle`, `FollowUps`), nicht in `MyCommon.Catalog` (abweichend von 9 und 13). myLife braucht sie erst für den Abgleich in M5; vorher ziehen sie nach my-common um. |
| 118 | Lebenszyklus | Muster je Typ wie in objekttypen.md, Abschnitt 3; Typen ohne Muster (Kontakt, Ablageort, Persönliche Daten, Steuern …) haben keinen Zustand. Neue Einträge starten in *aktiv*, *gültig* bzw. *vorhanden*. Wechsel von Hand nur entlang des Musters, Zurücknehmen eingeschlossen (gekündigt → aktiv, archiviert → beendet). *gekündigt*, *beendet* und *abgegeben* brauchen ein Datum, *ersetzt* einen Nachfolger derselben Art. *läuft ab* und *abgelaufen* setzt nur der Dienst (M2.2). Archivierte Einträge verschwinden aus den Listen (Schalter „Archiv“ in der API), die Suche findet sie. Bestehende Einträge bekommen per Migration ihren Anfangszustand. |
| 119 | Folgevorgänge | gekündigt → „Kündigungsbestätigung ablegen“ (14 Tage); beendet → „Letzte Abrechnung prüfen“ und „Lastschrift prüfen“ (30 Tage); läuft ab → Frist „Verlängern oder ersetzen?“; abgelaufen → „Ersatz anlegen“ (7 Tage); abgegeben → „Zugehörige Verträge prüfen“ mit allen Einträgen, die per Verweisfeld hierher zeigen. Zuständig ist die zuständige Person des Eintrags. Ein noch offener Vorgang derselben Regel am selben Eintrag wird nicht verdoppelt. Titel automatischer Vorgänge erscheinen in der Sprache des Lesers. |
| 120 | Vorgänge und Kommentare | Vorgang mit Art, Titel, Notiz, Zuständigkeit (Person mit Konto), Fälligkeit, Priorität, Status und Einträgen (0..n). Sichtbar ist ein Vorgang nur, wenn **alle** seine Einträge sichtbar sind; ohne Eintrag gehört er dem Haushalt. Ändern darf, wer einen seiner Einträge bearbeiten darf, bzw. Mitglieder. „Wartet auf Dritte“ nur mit Datum in der Zukunft; Prüfung und Frist nur mit Ergebnis erledigt; Erledigtes lässt sich wieder öffnen. Seite „Vorgänge“ mit „Meine“/„Haushalt“, Abschnitt „Vorgänge“ und „Kommentare“ auf der Objektseite. Eigene Kommentare lassen sich löschen. |

## Runde 13 (27.11.2026, M2.2 Wiedervorlage)

| Nr. | Thema | Entscheidung |
| ---: | --- | --- |
| 121 | Prüfintervalle | Vorschlag aus objekttypen.md übernommen: Versicherung 12 Monate, Vertrag/Abo 6, Kredit 12, Bankkonto und Depot 12, Urkunde 24; alle anderen Typen ohne regelmäßige Prüfung. Gerechnet ab der letzten Prüfung, sonst ab dem Anlegen. Je Eintrag eigenes Intervall (0 = keine Prüfung) und fester nächster Termin. Knopf „Jetzt prüfen“. |
| 122 | Fristen aus Feldern | Versicherung: Hauptfälligkeit minus Kündigungsfrist, Vorgang 6 Wochen vorher. Vertrag: Ablaufdatum minus Kündigungsfrist, 4 Wochen vorher. Kredit: Ablaufdatum, 6 Monate vorher. Hauptfälligkeit und Kündigungsfrist sind im Katalog Freitext; gelesen werden „01.01.“, „1.1.2027“, „1. Januar“, ISO-Datum und „3 Monate“, „6 Wochen“, „30 Tage“ (auch Englisch). Was sich nicht lesen lässt, ergibt keine Frist. Eine Frist erzeugt je Termin genau einen Vorgang. |
| 123 | Ablauf | Einträge mit dem Muster „Gültigkeit“ wechseln 3 Monate vor dem Ablaufdatum auf *läuft ab*, am Ablaufdatum auf *abgelaufen*, jeweils mit Folgevorgang und Eintrag in der Historie. |
| 124 | Eskalation | Der Dienst läuft alle 15 Minuten und ist idempotent. Stufe 1 am Fälligkeitstag, 2 nach 7 Tagen, 3 nach 14 Tagen; eine Prüfung in Stufe 3 setzt „überfällig seit“ am Eintrag. Die Benachrichtigung zu den Stufen kommt mit M2.4. „Wartet auf Dritte“ kommt am Datum wieder, mit Kommentar. |
| 125 | Abschluss und Zurückstellen | Prüfung und Frist schließen nur mit Ergebnis; das setzt „zuletzt geprüft“, löscht „überfällig“ und (bei Prüfungen) einen festen Termin. Zurückstellen bis höchstens 30 Tage voraus und nur mit Begründung, die als Kommentar am Vorgang steht; die Eskalation beginnt neu, die Zahl der Rückstellungen bleibt sichtbar. |

## Runde 14 (27.11.2026, M2.3 Startseite)

| Nr. | Thema | Entscheidung |
| ---: | --- | --- |
| 126 | Blöcke der Startseite | Oben ein Umschalter „Meins“/„Haushalt“, darunter fünf Blöcke: (1) überfällig und bis Sonntag fällig, (2) Fristen der nächsten 90 Tage aus Frist-Vorgängen, Ablaufdaten und dem Vertragsende nach Kündigung, (3) regelmäßige Zahlungen als Summe je Monat und je Bereich, (4) Posteingang, (5) Zustand des Bestands: fehlende Pflicht- und empfohlene Angaben, seit über 12 Monaten nicht geprüft, offene Abgleiche mit myLife. Darunter wie bisher die Bereiche. Listen zeigen höchstens 20 Einträge, die Zahlen gelten für alle. |
| 127 | Meins | „Meins“ heißt: Vorgänge, für die ich zuständig bin, und Einträge, bei denen ich zuständig oder Eigentümer bin. „Haushalt“ zeigt alles, was ich sehen darf; private Einträge anderer zählen nie mit, auch nicht in den Summen. Vorgänge, die auf Dritte warten, erscheinen erst wieder an ihrem Datum. |
| 128 | Zahlungen | Umgerechnet auf den Monat: monatlich ×1, vierteljährlich ÷3, halbjährlich ÷6, jährlich ÷12; nur aktive Einträge. Die Veränderung zum Vorjahr folgt, sobald es eine Historie der Beträge gibt. Posteingang zeigt bis M3 null, der Abgleich mit myLife bis M5. Fällt die Übersicht aus, bleiben die Bereiche nutzbar. |

## Runde 15 (27.11.2026, M2.4 Benachrichtigungen und Einstellungen)

| Nr. | Thema | Entscheidung |
| ---: | --- | --- |
| 129 | Einstellungsseite | **Verwaltung → Einstellungen**: Adresse der Instanz, SMTP (eigenes Konto, getrennt von myLife), ntfy, größte Datei, Uhrzeit und Generationen der Sicherung. Gespeichert in `system_settings`, SMTP-Passwort und ntfy-Token mit Data Protection verschlüsselt; sie gehen nie an den Browser. Bis zum ersten Speichern gelten die Umgebungsvariablen. Die Dateigröße lässt sich nur bis zur Grenze aus der Umgebung einstellen, weil diese beim Start die Größe der Anfragen festlegt (Nginx: 110m). Der Sicherungsschlüssel bleibt in der `.env`. Die Knöpfe zum Testen speichern zuerst und senden sofort, damit ein Fehler gleich zu sehen ist. |
| 130 | Mail-Outbox | Wie myLife: Die Mail wird in derselben Transaktion wie ihr Anlass gespeichert und im Hintergrund verschickt. Bei Fehlern wiederholt myDocs mit wachsendem Abstand (1 Minute bis 1 Tag, 10 Versuche, gut zwei Tage) und gibt dann auf, mit Eintrag „Mail nicht zustellbar“ im Protokoll. Die Parameter liegen verschlüsselt und werden nach dem Versand gelöscht. Die Texte stehen in den Sprachdateien der Oberfläche (`Mail_*`), die Sprache gilt je Konto. |
| 131 | Eigene Benachrichtigungen | **Benutzermenü → Benachrichtigungen**: Sprache der Mails, Zusammenfassung an oder aus und ihre Stunde, dazu ein eigenes ntfy-Thema (Knopf „Zufällig“). Voreingestellt ist 7 Uhr, auch für bestehende Konten; neue Konten bekommen Deutsch. |
| 132 | Tägliche Zusammenfassung | Einmal am Tag ab der gewählten Stunde in Ortszeit (`TZ`), geprüft alle 5 Minuten. Inhalt: eigene Vorgänge und, für Mitglieder, Vorgänge ohne Zuständigen, jeweils überfällig oder in den nächsten 7 Tagen fällig; dazu Vorgänge anderer ab Stufe 2, also seit 7 Tagen überfällig (14). Vorgänge, die auf Dritte warten, fehlen. Ist nichts fällig, kommt keine Mail. Mails nennen die Titel von Vorgang und Eintrag, aber keine Feldwerte und keine Dokumente. |
| 133 | Sofort bei Stufe 3 | Erreicht ein Vorgang Stufe 3 (14 Tage überfällig), geht sofort eine Mail und eine Push-Nachricht an alle Mitglieder mit Konto, die ihn sehen dürfen. Bei privaten Einträgen sind das nur der Eigentümer und wer eine Freigabe hat. Gäste bekommen nichts. Je Vorgang nur einmal. |
| 134 | ntfy | Push-Nachrichten enthalten nur Zahlen und einen Link, keine Titel: Wer den Namen eines Themas kennt, kann es lesen. Ein Fehler beim Push landet im Protokoll des Containers und wird nicht wiederholt. |
| 135 | Aktivierung per Mail | Ist SMTP eingerichtet, geht der Aktivierungslink beim Anlegen und beim Zurücksetzen zusätzlich per Mail an das Konto (86); der Administrator sieht ihn weiterhin. Der Link beginnt mit der Adresse der Instanz, ohne sie mit der Adresse der Anfrage. |

## Runde 16 (28.11.2026, M3.1 Worker und M3.2 Volltext)

| Nr. | Thema | Entscheidung |
| ---: | --- | --- |
| 136 | Worker ohne Schlüssel | `mydocs-worker` ist **zustandslos**: keine Datenbank, kein Volume, kein Hauptschlüssel. Die App liest den Auftrag aus der Tabelle, entschlüsselt die Datei beim Senden und schickt sie per HTTP an den Worker, der PDF, Text oder Vorschaubild zurückgibt; die App verschlüsselt die Antwort beim Lesen. Ändert 24 und die Architektur (dort holte der Worker selbst aus der Tabelle): So bleibt der Hauptschlüssel allein in der App. Der Worker hängt nur im internen Netz `worker` (`internal: true`) und erreicht das Internet nicht, damit Office-Dateien nichts nachladen. Dateisystem nur lesbar, Arbeitsdateien im tmpfs `/tmp` (768 MB), Speicher 1,5 GB (43). Eine Datei gleichzeitig. Ohne `MyDocs__WorkerUrl` werden Dokumente gespeichert, aber nicht verarbeitet. |
| 137 | Aufträge je Inhalt | Ein Auftrag, ein Text und die Ableitungen gehören zum **Inhalt** (Blob), nicht zur Version: Dieselbe Datei wird nur einmal verarbeitet, ihr Text steht sofort bei jedem weiteren Dokument. Ableitungen sind selbst verschlüsselte Blobs mit Referenzzähler; leert der Papierkorb einen Inhalt, verschwinden Ableitungen, Text und Auftrag mit. Die Migration stellt alle vorhandenen Dokumente in die Warteschlange. |
| 138 | Ablauf | Die Art erkennt die App an den ersten Bytes (PDF, JPEG, PNG, TIFF; Office als Zip/OLE nur mit passender Endung; RTF; Text nur als .txt/.csv/.md), nicht am Typ des Browsers. **PDF:** hat es Text (mindestens 30 Buchstaben je Seite), bleibt es, wie es ist; sonst OCR. **Bild:** img2pdf auf A4, dann OCR; scheitert die Erkennung, bleibt das Bild-PDF ohne Text. **Office und Text:** LibreOffice zu PDF. OCR mit OCRmyPDF: Deutsch und Englisch, Seiten drehen, gerade richten, kleine Bilder auf 300 dpi, Seiten mit Text bleiben unberührt, Ausgabe PDF/A. Danach Text (pdftotext) und Vorschaubild der ersten Seite (480 Pixel). Andere Dateien werden nur gespeichert. **Das Original bleibt immer**, der Download liefert es. |
| 139 | Fehler | Lehnt der Worker einen Schritt ab (Werkzeug mit Fehler, Zeit überschritten: OCR 30, Office 5 Minuten), geht es mit den übrigen Schritten weiter; der Fehler steht am Auftrag, ohne Inhalt der Datei. Ist der Worker nicht erreichbar, kommt der Auftrag nach 1, 10, 60 Minuten, 6 Stunden und 1 Tag wieder und gilt nach 6 Versuchen als fehlgeschlagen. **Verwaltung → Einstellungen → Verarbeitung** zeigt, ob der Worker bereit ist, und die Zahl der wartenden, fertigen und fehlgeschlagenen Aufträge, ohne Dateinamen; ein Knopf verarbeitet die fehlgeschlagenen neu. |
| 140 | Vorschau | Neue Endpunkte `preview` und `thumbnail`. Die Vorschau zeigt das PDF aus dem Worker (Office, Scans mit Textebene), sonst das Original, wenn es in den sicheren Typen steht (101). Öffnen der Vorschau steht wie der Download im Protokoll. Vorschaubilder erscheinen in der Liste der Dokumente; sie stehen wie Listen nicht im Protokoll. Solange ein Dokument wartet, steht „Wird verarbeitet …“ daneben. |
| 141 | Volltext über den Inhalt | Der erkannte Text der **aktuellen Version** steht in `documents.content_text` (höchstens 300 000 Zeichen, etwa 100 Seiten) mit eigenem Volltextindex (`german`) und Trigramm-Index. Die Suche findet ihn über Wortstämme und Teilwörter; die Tippfehler-Ähnlichkeit gilt nur für Titel, Angaben und Dateinamen, weil sie jeden langen Text ganz lesen müsste. Er zählt in der Rangfolge halb so viel wie die Angaben. Als Fundstelle zeigt die Suche das erste Vorkommen mit etwas Umgebung. Die Rechte gelten wie bisher in der Abfrage (26). |

## Runde 17 (28.11.2026, M3.3 Posteingang)

| Nr. | Thema | Entscheidung |
| ---: | --- | --- |
| 142 | Wem der Posteingang gehört | Jedes Mitglied hat einen **privaten** Posteingang, dazu gibt es einen des **Haushalts**. Den privaten sieht und bearbeitet nur seine Person, den des Haushalts jedes Mitglied; Gäste haben keinen. Die Admin-Einsicht gilt hier nicht, weil noch nichts einem Eintrag gehört. Hochladen ohne Eintrag geht in den eigenen Posteingang (Schalter „In den Posteingang des Haushalts“), Kamera ebenso, Mail in den des Absenders (49), der Scan-Ordner je nach Unterordner (M3.5). Die Suche findet Dokumente im eigenen und im gemeinsamen Posteingang; die Startseite zählt bei „Meins“ den eigenen, bei „Haushalt“ beide. |
| 143 | Fällig | Liegt das älteste Dokument eines Posteingangs länger als **7 Tage** (schließt „X Tage“ aus 20), legt die Wiedervorlage einen Vorgang „Posteingang abarbeiten“ an, einen je Posteingang, zuständig ist seine Person bzw. niemand beim Haushalt. Er eskaliert wie jeder Vorgang und schließt sich selbst, sobald der Posteingang leer ist. Den Vorgang zu einem privaten Posteingang sieht nur seine Person, auch in Zusammenfassung und Eskalation. |
| 144 | Zuordnen | Titel, **Dokumentart** (Rechnung, Schreiben, Vertrag, Police, Bescheid, Abrechnung/Kontoauszug, Kündigung/Bestätigung, Urkunde/Nachweis, Beleg, Gehaltsabrechnung, Sonstiges), Datum und **mindestens ein Eintrag**, den die Person bearbeiten darf; auf Wunsch ein Vorgang dazu (Art „Aufgabe“, zuständig ist, wer zuordnet, verknüpft mit denselben Einträgen). Danach gelten die Rechte der Einträge (100); das Protokoll des Eigentümers zeigt „Dokument zugeordnet“. Die Dokumentart lässt sich auch an jedem Dokument ändern. Der Typ „Korrespondenz“ für Dokumente ohne passenden Eintrag (21) kommt mit den Zusatztypen; bis dahin braucht jedes Dokument einen bestehenden Eintrag. |
| 145 | Weitergeben, verwerfen | Ein privates Dokument gibt seine Person an den Haushalt; eines aus dem Haushalt übernimmt jedes Mitglied für sich. **Verwerfen** legt es in den Papierkorb: 30 Tage unter „Verworfene“ mit „Zurückholen“, dann endgültig gelöscht. Dokumente im Posteingang leert der Papierkorb nicht, obwohl sie keinen Eintrag haben. |

## Runde 18 (28.11.2026, M3.4 Kamera)

| Nr. | Thema | Entscheidung |
| ---: | --- | --- |
| 146 | Kamera | Seite **Posteingang → Kamera** (`/inbox/camera`): „Seite fotografieren“ öffnet am Handy die Rückkamera (`capture="environment"`), „Fotos wählen“ nimmt vorhandene Bilder. Jede Seite lässt sich verschieben, in 90-Grad-Schritten drehen und an jedem Rand um bis zu 45 % grob zuschneiden. Der Browser gibt jede Seite als JPEG aus (längere Kante höchstens 3000 Pixel, Qualität 0,85); die Bilder bleiben bis dahin nur im Speicher des Browsers. Der **Server baut daraus das PDF** selbst, ohne die Bilder neu zu kodieren, jede Seite so groß, dass sie in A4 passt; das ist das Original. Gerade richten, Drehen nach Text und OCR macht der Worker wie bei jedem Scan (138). Höchstens 50 Seiten, zusammen höchstens so groß wie eine Datei (78); nur Graustufen- und RGB-JPEG. Das Dokument heißt „Kamera <Datum> <Uhrzeit>“ und landet im eigenen Posteingang oder, per Schalter, in dem des Haushalts. |

## Runde 19 (28.11.2026, M3.5 Scan-Ordner)

| Nr. | Thema | Entscheidung |
| ---: | --- | --- |
| 147 | Scan-Ordner | Ein Ordner der Synology wird als `/scan` eingebunden (`MYDOCS_SCAN_DIR`) und alle 30 Sekunden durchgesehen. Dateien **direkt darin** kommen in den Posteingang des **Haushalts**; ein **Unterordner, der wie die E-Mail-Adresse eines Kontos heißt**, geht in dessen privaten Posteingang (Groß- und Kleinschreibung egal). So bekommt jede Person am Scanner ein eigenes Ziel, ohne Einstellung in myDocs. Übernommen wird eine Datei, wenn Größe und Änderungszeit bei zwei Durchläufen gleich sind; danach **wird sie gelöscht**, damit kein Klartext liegen bleibt. Nur PDF und Bilder (JPEG, PNG, TIFF), erkannt am Inhalt, bis zur größten Datei (78); anderes kommt nach `_fehler`. Unbekannte Unterordner, versteckte Dateien und die Ordner der Synology (`@eaDir`, `#recycle`) bleiben unberührt. Lässt sich eine übernommene Datei nicht löschen, steht das im Protokoll des Containers und sie wird nicht ein zweites Mal übernommen. Der Papierkorb der Freigabe muss aus sein. |

## Offen

| Thema | Was fehlt |
| --- | --- |
| Zusatztypen (7) | Felder, Bereich und Lebenszyklus-Muster je Typ |
| Aufbewahrung (28) | Dauer und Bezugspunkt je Dokumentart |
| Meldungen an Administratoren | Fehlgeschlagene Sicherung oder unzustellbare Mails per Mail melden (bisher: Seite „Sicherung“ und Protokoll) |
