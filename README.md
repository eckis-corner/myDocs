# myDocs

**Dokumentenmanagement für den privaten Haushalt – selbst gehostet, mit Wiedervorlage, die nichts liegen lässt.**

myDocs verwaltet Versicherungen, Konten, Verträge, Abos, Urkunden und alles andere, was ein Haushalt an Papier und Pflichten hat, als eigenständige **Objekte** mit ihren Dokumenten. Jedes Objekt hat eine zuständige Person und eine **Wiedervorlage**, die eine Prüfung erzwingt. Ein **Lebenszyklus** und **Vorgänge** steuern die Bearbeitung. Office-Dokumente bleiben lebendig: öffnen, ändern, speichern, und jede Fassung wird zur Version.

myDocs ist der Alltag, [myLife](https://github.com/eckis-corner/myLife) der Notfall. Was im Ernstfall gebraucht wird, übernimmt myLife per Abgleich aus myDocs, verschlüsselt im Browser.

> **Status:** Planung. Die Umsetzung beginnt nach Freigabe des Plans.

## Merkmale (geplant)

- **Objekte statt Ordner:** Typen aus dem gemeinsamen Katalog mit myLife, Formular und Datenblatt aus dem Schema
- **Wiedervorlage mit Zwang:** Regeln je Typ (Prüfintervall, letzter Kündigungstermin, Ablauf), Eskalation, Ergebnis als Pflicht
- **Lebenszyklus und Vorgänge:** Vertrag, Gültigkeit, Bestand, mit automatischen Folgevorgängen
- **Posteingang:** Upload, Kamera, Scanner, Mail, Desktop-Ordner; OCR und Volltextsuche
- **Office-Dokumente mit Versionen:** Check-out/Check-in über den Desktop-Client
- **Haushalt und Privates:** gemeinsamer Bestand, private Objekte, Freigaben, Audit-Log
- **Clients:** Web (Blazor WebAssembly, MudBlazor) und Desktop für Windows, macOS und Linux (Photino.Blazor)
- **Self-hosted:** Docker Compose auf einer Synology, PostgreSQL, Verschlüsselung at rest
- Deutsch und Englisch

## Dokumentation

| Dokument | Inhalt |
| --- | --- |
| [docs/architektur.md](docs/architektur.md) | Komponenten, Anmeldung, Dokumente, Suche, Brücke zu myLife, Datenmodell |
| [docs/objekttypen.md](docs/objekttypen.md) | Lebenszyklus-Muster, Zusatztypen, Prüfregeln, Aufbewahrung |
| [docs/entscheidungen.md](docs/entscheidungen.md) | Entscheidungsprotokoll und offene Punkte |
| [docs/meilensteine.md](docs/meilensteine.md) | Umsetzungsplan |
| [PROMPT.md](PROMPT.md) | Ursprüngliche Idee |

## Lizenz

[GNU Affero General Public License v3.0](LICENSE) (Entscheidung 54)
