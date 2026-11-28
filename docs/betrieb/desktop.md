# Desktop-Client einrichten

Der Desktop-Client zeigt dieselbe Oberfläche wie der Browser und kann zusätzlich Office-Dateien **bearbeiten** (Check-out und Check-in) und einen **Eingangsordner** beobachten. Er läuft unter Windows, macOS und Linux.

## 1. Herunterladen

GitHub → `eckis-corner/myDocs` → **Releases** (oder bei einem Testbuild: Actions → CI → Artefakte):

| System | Datei | Installieren |
| --- | --- | --- |
| Windows 10/11 (x64) | `myDocs-<Version>-win-x64.zip` | Entpacken, etwa nach `C:\Programme\myDocs`, und `myDocs.exe` starten. Windows SmartScreen warnt beim ersten Start, weil das Programm nicht signiert ist: **Weitere Informationen → Trotzdem ausführen**. WebView2 ist in Windows 11 enthalten; unter Windows 10 ggf. die „WebView2 Runtime“ von Microsoft installieren. |
| macOS 12+ (Apple Silicon) | `myDocs-<Version>-osx-arm64.zip` | Entpacken, `myDocs.app` in „Programme“ ziehen. Weil die App nicht signiert ist, beim ersten Mal **Rechtsklick → Öffnen → Öffnen**. Hilft das nicht: im Terminal `xattr -dr com.apple.quarantine /Applications/myDocs.app`. |
| macOS (Intel) | `myDocs-<Version>-osx-x64.zip` | wie oben |
| Linux (x64) | `myDocs-<Version>-linux-x64.tar.gz` | `tar -xzf …` und `myDocs/myDocs` starten. Gebraucht werden WebKitGTK 4.1, GTK 3, libnotify und für das Token `secret-tool`, unter Debian/Ubuntu: `sudo apt install libwebkit2gtk-4.1-0 libgtk-3-0 libnotify4 libsecret-tools` |

## 2. Verbinden

1. Adresse der Instanz eingeben, etwa `https://mydocs.eckis-corner.de`, und **Im Browser anmelden**.
2. Im Browser wie gewohnt anmelden (Passwort und zweiter Faktor) und bei „Gerät anmelden“ **Erlauben**.
3. Zurück im Client erscheint die Übersicht. Das Gerät steht nun unter **Benutzermenü → Geräte** in der Web-App und lässt sich dort widerrufen.

Der Client merkt sich die Anmeldung im Schlüsselspeicher des Systems (Anmeldeinformationsverwaltung, Schlüsselbund bzw. GNOME-Schlüsselbund/KWallet). **Abmelden** im Benutzermenü widerruft das Gerät.

## 3. Office-Dateien bearbeiten

- Bei einem Dokument **Bearbeiten** (Stift-Symbol): Die Datei wird ausgecheckt und mit der Standard-App geöffnet. Andere sehen „In Bearbeitung von …“.
- **Jedes Speichern** wird eine neue Version; eine Benachrichtigung bestätigt es.
- Ohne Netz weiterarbeiten geht: Die Änderung wird hochgeladen, sobald myDocs erreichbar ist, auch nach einem Neustart des Clients.
- **Fertig** (Menü am Dokument oder Seite **In Bearbeitung**) lädt die letzte Fassung hoch und gibt das Dokument frei.
- Hat jemand das Dokument in der Zwischenzeit geändert (etwa nach Aufheben der Sperre), wird nichts überschrieben: Die eigene Fassung erscheint als „… (Konflikt …)“ daneben.
- Die Sperre läuft nach 24 Stunden ohne Lebenszeichen ab. Wer sie hält, kann sie auch in der Web-App aufheben, ebenso jeder Administrator.

## 4. Eingangsordner

Unter **Posteingang** einen Ordner wählen. Dateien, die dort abgelegt werden (Scan-Software, Downloads), gehen in den eigenen Posteingang und wandern danach in den Unterordner `hochgeladen`. Der Ordner wird nur beobachtet, solange der Client läuft.

## 5. Wo die Daten liegen

| System | Ordner |
| --- | --- |
| Windows | `%APPDATA%\myDocs` |
| macOS | `~/.config/myDocs` |
| Linux | `~/.config/myDocs` |

Darin `settings.json` (Adresse, Gerät, Eingangsordner; **keine** Tokens), `checkouts.json` und der Ordner `work` mit den ausgecheckten Dateien im Klartext. Der Ordner ist nur für das eigene Konto lesbar; die Festplatte sollte verschlüsselt sein (BitLocker, FileVault, LUKS). Schreibgeschützt geöffnete Kopien in `open` löscht der Client beim nächsten Start.

## 6. Entfernen

Erst **Abmelden** (widerruft das Gerät), dann Programm und den Datenordner löschen. Ist der Rechner weg: in der Web-App unter **Geräte** widerrufen und ggf. als Administrator die Sperren seiner Dokumente aufheben.
