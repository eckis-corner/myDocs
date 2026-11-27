# Installation auf einer Synology mit Nginx Proxy Manager

Für DSM 7.2 und neuer mit **Container Manager** (Synology DS1520+, x86-64), neben myLife. Für andere Docker-Hosts gelten dieselben Schritte sinngemäß.

> **Stand M1 (Grundgerüst):** Einrichtung, Anmeldung mit Authenticator-App oder Passkey, Benutzer und Haushalt, Einträge aus dem Katalog, Dokumente mit Versionen, Suche, Protokoll, Admin-Einsicht, Papierkorb, tägliche Sicherung. Noch nicht: Wiedervorlage und Vorgänge (M2), Mailversand (M2), Posteingang und OCR (M3), Desktop-Client (M4), Abgleich mit myLife (M5). Vor echten Daten die Abnahme durchgehen: [abnahme.md](abnahme.md).

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
5. Fertigstellen. Der Container Manager lädt `ghcr.io/eckis-corner/mydocs` und `postgres:17-alpine` und startet beide.

Kontrolle im LAN: `http://<IP-der-Synology>:8090/health/ready` liefert `Healthy`.

> Meldet der Download des Images `unauthorized`, ist das Paket auf GitHub noch privat: GitHub → Profil → Packages → `mydocs` → Package settings → Change visibility → Public.

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

Der Assistent führt durch Konto, Passwort (mindestens 12 Zeichen), Authenticator-App (QR-Code) und **10 Wiederherstellungscodes** (ausdrucken oder im Passwortmanager speichern). Unter **Benutzermenü → Sicherheit** lässt sich danach ein **Passkey** hinzufügen; er ersetzt bei der Anmeldung den Code der App. Danach legt der Administrator unter **Verwaltung → Benutzer** weitere Konten an und gibt jedem den angezeigten **Aktivierungslink** weiter (einmal gültig, 7 Tage).

## 7. Hauptschlüssel und Sicherung

### Hauptschlüssel ausdrucken

Unter **Verwaltung → Sicherung → Hauptschlüssel** den Code der Authenticator-App eingeben: myDocs zeigt den Hauptschlüssel als **24 Wörter**. Ausdrucken und zusammen mit dem Ausdruck von `MYDOCS_BACKUP_KEY` getrennt von der NAS aufbewahren, etwa beim Notfallordner. Die Anzeige steht im Protokoll.

**Ohne Hauptschlüssel sind alle Dokumente verloren**, auch die in den Sicherungen. In der Sicherung ist er zwar enthalten, aber nur zusammen mit dem Sicherungsschlüssel nutzbar.

### Was myDocs selbst sichert

Ist `MYDOCS_BACKUP_KEY` gesetzt, legt myDocs **jeden Tag** um 3 Uhr eine verschlüsselte Sicherung unter `data/app/backup/mydocs-<Datum>-<Uhrzeit>.mdbak` ab:

- die **Datenbank** als konsistenter Schnappschuss,
- den Ordner `data/app/keys` (Schlüsselring von Data Protection und **Hauptschlüssel**),
- die **Dokumente** aus `data/app/files`, so verschlüsselt, wie sie dort liegen.

Aufbewahrt werden die neueste Sicherung je Tag (7), je Woche (4) und je Monat (6). Unter **Verwaltung → Sicherung** stehen letzte Sicherung, nächster Lauf, ein Fehler und der Knopf **Jetzt sichern**. Uhrzeit und Generationen lassen sich über Umgebungsvariablen ändern: `MyDocs__BackupHour`, `MyDocs__BackupKeepDaily`, `MyDocs__BackupKeepWeekly`, `MyDocs__BackupKeepMonthly`, `MyDocs__BackupIncludeDocuments`.

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

oder im Container Manager: Projekt stoppen, Image `ghcr.io/eckis-corner/mydocs` aktualisieren, Projekt starten. Datenbank-Migrationen laufen beim Start automatisch.

## 9. Lokale Entwicklung und eigener Build

```bash
cp .env.example .env        # für die Entwicklung: POSTGRES_PASSWORD=mydocs
docker compose -f docker-compose.yml -f compose.dev.yml up -d db   # nur die Datenbank (localhost:5433)
dotnet run --project src/MyDocs.Server                             # http://localhost:5072
```

Ein eigenes Image braucht die Anmeldung an GitHub Packages für `MyCommon.*` (siehe `nuget.config`):

```bash
export NuGetPackageSourceCredentials_eckiscorner="Username=<github-name>;Password=<token mit read:packages>"
docker compose -f docker-compose.yml -f compose.dev.yml up -d --build
```
