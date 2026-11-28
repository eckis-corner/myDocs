# Installation auf einer Synology mit Nginx Proxy Manager

Für DSM 7.2 und neuer mit **Container Manager** (Synology DS1520+, x86-64), neben myLife. Für andere Docker-Hosts gelten dieselben Schritte sinngemäß.

> **Stand M3.5:** Einrichtung, Anmeldung mit Authenticator-App oder Passkey, Benutzer und Haushalt, Einträge aus dem Katalog, Dokumente mit Versionen, Suche, Protokoll, Admin-Einsicht, Papierkorb, tägliche Sicherung; Lebenszyklus, Vorgänge, Wiedervorlage mit Eskalation, Startseite und Benachrichtigungen per Mail und ntfy; dazu der Worker für Texterkennung, Office-Vorschau und Vorschaubilder mit Volltextsuche im Dokumenttext und der Posteingang mit Kamera und Scan-Ordner. Noch nicht: Mail-Eingang (Rest von M3), Desktop-Client (M4), Abgleich mit myLife (M5). Vor echten Daten die Abnahme durchgehen: [abnahme.md](abnahme.md).

## 1. Ordner anlegen

In der File Station im freigegebenen Ordner `docker` den Ordner `mydocs` anlegen, **getrennt von `mylife`**:

```text
/volume1/docker/mydocs/
├── docker-compose.yml     ← aus dem Repository
├── .env                   ← aus .env.example, angepasst
└── data/
    ├── app/               ← Dateien, Schlüssel, Sicherungen der App
    └── postgres/          ← Datenbank
```

`docker-compose.yml` und `.env.example` liegen im Repository im Hauptverzeichnis. Herunterladen und `.env.example` in `.env` umbenennen.

## 2. `.env` anpassen

| Variable | Wert |
| --- | --- |
| `POSTGRES_PASSWORD` | Langes Zufallspasswort, z. B. per SSH `openssl rand -hex 24`. Danach nicht mehr ändern. |
| `MYDOCS_VERSION` | `latest` oder eine feste Version |
| `MYDOCS_PORT` | Freier Port auf der Synology, z. B. `8090` (myLife nutzt einen anderen, etwa `8089`) |
| `MYDOCS_DATA_DIR` | `./data` (relativ zum Projektordner) |
| `MYDOCS_BACKUP_KEY` | Schlüssel der täglichen Sicherung: per SSH `openssl rand -base64 32`. **Ausdrucken und getrennt von der NAS aufbewahren**; ohne ihn lässt sich keine Sicherung zurückspielen. Leer = keine automatische Sicherung. |
| `MYDOCS_SCAN_DIR` | Ordner, in den der Scanner schreibt, z. B. `/volume1/scan/mydocs` (Abschnitt 6b). Ohne Scanner `./data/scan` lassen. |
| `TZ` | `Europe/Berlin` |

## 3. Rechte für den App-Ordner setzen (einmalig, per SSH)

Die App läuft im Container nicht als root, sondern als Benutzer mit der UID **1654**. Der Ordner `data/app` muss ihm gehören:

```bash
sudo mkdir -p /volume1/docker/mydocs/data/app /volume1/docker/mydocs/data/postgres
sudo chown -R 1654:1654 /volume1/docker/mydocs/data/app
```

(SSH aktivieren: Systemsteuerung → Terminal & SNMP → SSH-Dienst aktivieren.)

## 4. Projekt im Container Manager anlegen

1. **Container Manager → Projekt → Erstellen**
2. Projektname `mydocs`, Pfad `/volume1/docker/mydocs`
3. Quelle: **Vorhandene docker-compose.yml verwenden**
4. Web-Portal-Einstellungen überspringen (das übernimmt der Nginx Proxy Manager)
5. Fertigstellen. Der Container Manager lädt `ghcr.io/eckis-corner/mydocs`, `ghcr.io/eckis-corner/mydocs-worker` und `postgres:17-alpine` und startet alle drei.

Zusammen brauchen die drei Container bis zu 2,5 GB Arbeitsspeicher: App 512 MB, Datenbank 512 MB, Worker 1,5 GB (davon bis 768 MB für Arbeitsdateien im Speicher, damit Klartext nie auf die Platte kommt). Der Worker hat kein Volume, keine Schlüssel und keinen Zugang zum Internet; er erreicht nur die App über das interne Netz `worker`.

Kontrolle im LAN: `http://<IP-der-Synology>:8090/health/ready` liefert `Healthy`.

> Meldet der Download eines Images `unauthorized`, ist das Paket auf GitHub noch privat: GitHub → Profil → Packages → `mydocs` bzw. `mydocs-worker` → Package settings → Change visibility → Public.

Kontrolle des Workers: **Verwaltung → Einstellungen → Verarbeitung** zeigt „Der Worker ist bereit.“ Beim ersten Start mit Worker werden alle schon vorhandenen Dokumente einmal verarbeitet; das kann bei vielen Scans einige Zeit dauern.

Beim ersten Start legt myDocs den **Hauptschlüssel** `data/app/keys/master.key` an (Abschnitt 7). Er verschlüsselt alle Dokumente.

## 5. Nginx Proxy Manager

**Hosts → Proxy Hosts → Add Proxy Host**

| Reiter | Einstellung |
| --- | --- |
| Details | Domain Names: `mydocs.eckis-corner.de` · Scheme: `http` · Forward Hostname/IP: IP der Synology · Forward Port: `8090` · *Block Common Exploits* an · *Websockets Support* an |
| SSL | *Request a new SSL Certificate* (Let's Encrypt) · *Force SSL* an · *HTTP/2 Support* an · *HSTS Enabled* an |
| Advanced | `client_max_body_size 110m;` – Dateien sind bis 100 MB groß. Ohne diese Zeile lehnt nginx größere Uploads ab. |

Die App nimmt `X-Forwarded-*`-Header nur aus privaten Netzen an (10.x, 172.16–31.x, 192.168.x). Läuft der Proxy in einem anderen Netz, dieses zusätzlich über die Umgebungsvariable `MyDocs__TrustedProxyNetworks__5=<Netz/Präfix>` in der Compose-Datei eintragen.

## 6. Ersteinrichtung

Beim ersten Aufruf von `https://mydocs.eckis-corner.de` startet der Einrichtungsassistent. Er fragt nach dem **Einrichtungscode**, den der Server beim Start ins Protokoll schreibt:

**Container Manager → Container → `mydocs-app-1` → Protokoll** (oder per SSH `sudo docker logs mydocs-app-1`), Zeile:

```text
myDocs ist noch nicht eingerichtet. Einrichtungscode für den Assistenten: ABCD-EFGH-JKLM-NPQR-STUV
```

Nach jedem Neustart des Containers gilt ein neuer Code. Sobald das erste Konto mit Authenticator-App angelegt ist, gibt es keinen Code mehr.

Der Assistent führt durch Konto, Passwort (mindestens 12 Zeichen), Authenticator-App (QR-Code) und **10 Wiederherstellungscodes** (ausdrucken oder im Passwortmanager speichern). Unter **Benutzermenü → Sicherheit** lässt sich danach ein **Passkey** hinzufügen; er ersetzt bei der Anmeldung den Code der App. Danach legt der Administrator unter **Verwaltung → Benutzer** weitere Konten an und gibt jedem den angezeigten **Aktivierungslink** weiter (einmal gültig, 7 Tage). Ist der Mailversand eingerichtet (Abschnitt 6a), geht der Link zusätzlich per Mail an das Konto.

## 6a. Mailversand und Push-Nachrichten

Unter **Verwaltung → Einstellungen**:

- **Adresse von myDocs:** `https://mydocs.eckis-corner.de`. Sie steht in den Links der Mails.
- **Mailversand:** ein **eigenes** SMTP-Konto für myDocs, nicht das von myLife. Typisch ist Port 587 mit STARTTLS oder Port 465 mit SSL/TLS, dazu Benutzername, Passwort und Absenderadresse. **Speichern und Testmail an mich** schickt sofort eine Mail an die eigene Adresse und zeigt einen Fehler direkt an.
- **Push-Nachrichten (optional):** Adresse eines ntfy-Servers (eigener oder `https://ntfy.sh`), bei Bedarf mit Zugangstoken. Zum Testen ein Thema eintragen, das in der ntfy-App abonniert ist.

Passwort und Token liegen verschlüsselt in der Datenbank und erscheinen nie wieder im Browser. Leer lassen behält sie.

Jede Person stellt unter **Benutzermenü → Benachrichtigungen** ein, wann ihre **tägliche Zusammenfassung** kommt (voreingestellt 7 Uhr, abschaltbar), in welcher Sprache und an welches eigene ntfy-Thema. Ist ein Vorgang 14 Tage überfällig, geht sofort eine Mail an alle Mitglieder, die ihn sehen dürfen.

Auf derselben Seite stehen die **größte Datei** (höchstens der Wert der Installation, 100 MB) und **Uhrzeit und Generationen der Sicherung**. Sie ersetzen die Umgebungsvariablen aus Abschnitt 7, sobald einmal gespeichert ist.

## 6b. Scan-Ordner

Der Scanner (oder das Multifunktionsgerät) legt Scans per SMB oder FTP in einem Ordner der Synology ab; myDocs holt sie alle 30 Sekunden in den Posteingang und **löscht sie dort**.

1. Systemsteuerung → Gemeinsamer Ordner: einen Ordner `scan` anlegen (falls es ihn nicht gibt) und darin den Unterordner `mydocs`. **Papierkorb für diesen Ordner ausschalten**, sonst bleiben gelöschte Scans als Klartext im `#recycle` liegen.
2. Per SSH dem Benutzer der App Schreibrechte geben:

   ```bash
   sudo chown -R 1654:1654 /volume1/scan/mydocs
   sudo chmod 2775 /volume1/scan/mydocs
   ```

   Der Scanner braucht ein eigenes Konto der Synology mit Schreibrecht auf diesen Ordner.
3. In der `.env`: `MYDOCS_SCAN_DIR=/volume1/scan/mydocs`, dann das Projekt neu starten.
4. Am Scanner Ziele anlegen:
   - `\\<Synology>\scan\mydocs` → **Posteingang des Haushalts**
   - `\\<Synology>\scan\mydocs\<E-Mail-Adresse des Kontos>`, etwa `…\mydocs\bea@example.de` → **privater Posteingang** dieser Person. Den Unterordner einmal anlegen; er muss genau wie die Anmeldeadresse heißen. **Danach den `chown`-Befehl aus Schritt 2 wiederholen**, sonst darf myDocs die Scans darin nicht löschen.
5. Kontrolle: **Verwaltung → Einstellungen → Verarbeitung** zeigt „Der Scan-Ordner wird beobachtet.“

Übernommen werden PDF und Bilder (JPEG, PNG, TIFF). Andere Dateien und zu große kommen in den Unterordner `_fehler`; dort löscht man sie von Hand.

## 7. Hauptschlüssel und Sicherung

### Hauptschlüssel ausdrucken

Unter **Verwaltung → Sicherung → Hauptschlüssel** den Code der Authenticator-App eingeben: myDocs zeigt den Hauptschlüssel als **24 Wörter**. Ausdrucken und zusammen mit dem Ausdruck von `MYDOCS_BACKUP_KEY` getrennt von der NAS aufbewahren, etwa beim Notfallordner. Die Anzeige steht im Protokoll.

**Ohne Hauptschlüssel sind alle Dokumente verloren**, auch die in den Sicherungen. In der Sicherung ist er zwar enthalten, aber nur zusammen mit dem Sicherungsschlüssel nutzbar.

### Was myDocs selbst sichert

Ist `MYDOCS_BACKUP_KEY` gesetzt, legt myDocs **jeden Tag** um 3 Uhr eine verschlüsselte Sicherung unter `data/app/backup/mydocs-<Datum>-<Uhrzeit>.mdbak` ab:

- die **Datenbank** als konsistenter Schnappschuss,
- den Ordner `data/app/keys` (Schlüsselring von Data Protection und **Hauptschlüssel**),
- die **Dokumente** aus `data/app/files`, so verschlüsselt, wie sie dort liegen.

Aufbewahrt werden die neueste Sicherung je Tag (7), je Woche (4) und je Monat (6). Unter **Verwaltung → Sicherung** stehen letzte Sicherung, nächster Lauf, ein Fehler und der Knopf **Jetzt sichern**. Uhrzeit und Generationen stellt der Administrator unter **Verwaltung → Einstellungen** ein. Bis zum ersten Speichern dort gelten die Umgebungsvariablen `MyDocs__BackupHour`, `MyDocs__BackupKeepDaily`, `MyDocs__BackupKeepWeekly`, `MyDocs__BackupKeepMonthly` und `MyDocs__BackupIncludeDocuments`.

### Hyper Backup

Den **gesamten Ordner** `/volume1/docker/mydocs` (inklusive `.env` und `data/`) in **Hyper Backup** aufnehmen, am besten auf ein Ziel außerhalb der NAS. Die laufende Datenbank (`data/postgres`) ist dort nicht unbedingt konsistent; zurückgespielt wird deshalb aus einer myDocs-Sicherung.

### Zurückspielen

Die Rücksicherung **ersetzt die gesamte Datenbank**, die Dokumente und den Ordner `keys`.

1. In Container Manager das Projekt **stoppen**.
2. Per SSH im Projektordner:

   ```bash
   cd /volume1/docker/mydocs
   sudo docker compose up -d db
   sudo docker compose run --rm app restore /data/backup/mydocs-20261127-030000.mdbak --confirm
   ```

   Ohne `--confirm` passiert nichts. Die Sicherung muss mit demselben `MYDOCS_BACKUP_KEY` erstellt worden sein und lässt sich mit derselben oder einer neueren myDocs-Version zurückspielen.
3. Das Projekt wieder **starten** und sich anmelden.

Auf einer neuen NAS: zuerst wie oben installieren (dieselbe `.env` mit demselben Sicherungsschlüssel), die Sicherungsdatei nach `data/app/backup` kopieren, dann Schritt 2.

### Nur der Hauptschlüssel fehlt

Ist `data/app/keys/master.key` verloren, die Dateien aber noch da (etwa nach einem Fehler beim Kopieren), schreibt dieser Befehl den Schlüssel aus dem Ausdruck zurück:

```bash
sudo docker compose run --rm app master-key restore "wort1 wort2 … wort24" --confirm
```

## 8. Updates

Vor jedem Update unter **Verwaltung → Sicherung** auf **Jetzt sichern** klicken. Dann:

```bash
cd /volume1/docker/mydocs
sudo docker compose pull
sudo docker compose up -d
```

oder im Container Manager: Projekt stoppen, die Images `ghcr.io/eckis-corner/mydocs` und `ghcr.io/eckis-corner/mydocs-worker` aktualisieren, Projekt starten. Datenbank-Migrationen laufen beim Start automatisch.

**Update auf M3:** Die neue `docker-compose.yml` aus dem Repository übernehmen; sie enthält den Worker und das interne Netz. Danach wie oben `pull` und `up -d`.

## 9. Lokale Entwicklung und eigener Build

```bash
cp .env.example .env        # für die Entwicklung: POSTGRES_PASSWORD=mydocs
docker compose -f docker-compose.yml -f compose.dev.yml up -d db   # nur die Datenbank (localhost:5433)
dotnet run --project src/MyDocs.Server                             # http://localhost:5072
```

Den Worker lokal starten und in der App eintragen (ohne ihn speichert myDocs Dokumente, verarbeitet sie aber nicht):

```bash
docker build -f Dockerfile.worker -t mydocs-worker:dev .
docker run --rm -d --name mydocs-worker --read-only --tmpfs /tmp:size=768m -p 127.0.0.1:18080:8080 mydocs-worker:dev
MyDocs__WorkerUrl=http://localhost:18080 dotnet run --project src/MyDocs.Server
# Tests gegen die echten Werkzeuge:
MYDOCS_WORKER_URL=http://localhost:18080 dotnet test --project tests/MyDocs.Tests -- --filter-class MyDocs.Tests.WorkerIntegrationTests
```

Ein eigenes Image der App braucht die Anmeldung an GitHub Packages für `MyCommon.*` (siehe `nuget.config`):

```bash
export NuGetPackageSourceCredentials_eckiscorner="Username=<github-name>;Password=<token mit read:packages>"
docker compose -f docker-compose.yml -f compose.dev.yml up -d --build
```
