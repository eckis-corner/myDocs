# myDocs

**Dokumentenmanagement für den privaten Haushalt – selbst gehostet, mit Wiedervorlage, die nichts liegen lässt.**

myDocs verwaltet Versicherungen, Konten, Verträge, Abos, Urkunden und alles andere, was ein Haushalt an Papier und Pflichten hat, als eigenständige **Objekte** mit ihren Dokumenten. Jedes Objekt hat eine zuständige Person und eine **Wiedervorlage**, die eine Prüfung erzwingt. Ein **Lebenszyklus** und **Vorgänge** steuern die Bearbeitung. Office-Dokumente bleiben lebendig: öffnen, ändern, speichern, und jede Fassung wird zur Version.

myDocs ist der Alltag, [myLife](https://github.com/eckis-corner/myLife) der Notfall. Was im Ernstfall gebraucht wird, übernimmt myLife per Abgleich aus myDocs, verschlüsselt im Browser.

> **Status:** Meilenstein M1 (Grundgerüst) fertig, Abnahme auf der Synology steht aus ([Checkliste](docs/betrieb/abnahme.md)). Echte Daten erst ab M2.

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
| [docs/betrieb/installation.md](docs/betrieb/installation.md) | Installation auf der Synology, Sicherung, Rücksicherung, Hauptschlüssel, Updates |
| [docs/betrieb/abnahme.md](docs/betrieb/abnahme.md) | Checkliste für die Abnahme von M1 |
| [PROMPT.md](PROMPT.md) | Ursprüngliche Idee |

## Entwickeln

Voraussetzungen: .NET SDK 10, Docker (für PostgreSQL und die Integrationstests). `my-common` liegt am besten daneben (`eckis-corner/my-common`), dann nutzt der Build dessen Quellcode. Sonst kommen die Pakete aus GitHub Packages, siehe `nuget.config`.

```bash
cp .env.example .env                                               # für die Entwicklung: POSTGRES_PASSWORD=mydocs
docker compose -f docker-compose.yml -f compose.dev.yml up -d db   # PostgreSQL auf localhost:5433
dotnet run --project src/MyDocs.Server                             # http://localhost:5072
dotnet test --solution MyDocs.slnx                                 # Tests (nutzen Testcontainers)
python3 tools/resources.py                                         # Texte de/en neu erzeugen
```

**VS Code:** F5 („myDocs (Server)“) startet die Datenbank, baut und öffnet die App unter <http://localhost:5072>.

## Projektstruktur

```text
src/
  MyDocs.Shared     DTOs, API-Verträge, Sprachen
  MyDocs.ApiClient  typisierter HTTP-Client für die REST-API
  MyDocs.UI         gesamte Oberfläche (Razor Class Library, MudBlazor), auch für den späteren Desktop-Client
  MyDocs.Web        Hosting im Browser (Blazor WebAssembly)
  MyDocs.Server     ASP.NET Core: API, Auslieferung, PostgreSQL
tests/
  MyDocs.Tests      xUnit, bUnit, Integrationstests mit Testcontainers
```

## Sicherheit

Bitte Sicherheitslücken **nicht** als öffentliches Issue melden, siehe [SECURITY.md](SECURITY.md).

## Lizenz

[GNU Affero General Public License v3.0](LICENSE) (Entscheidung 54)
