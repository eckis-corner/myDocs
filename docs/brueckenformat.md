# Brückenformat myDocs → myLife, Version 1

Stand: 28.11.2026 (M5, Entscheidungen 162–171). myLife holt damit im Browser ab, was im Ernstfall gebraucht wird. Beide Apps haben eigene Klassen für dieses JSON (myDocs: `MyDocs.Shared.MyLife`, myLife: `MyLife.Client.Services.MyDocsBridge`). **Geändert wird nur durch Ergänzen**; wer ein Feld nicht kennt, übergeht es. Eine neue `version` heißt: myLife muss erst aktualisiert werden.

## Endpunkte

Alle unter `/api/mylife`, nur mit `Authorization: Bearer mdl_…` (Token aus **Benutzermenü → myLife**), ohne Cookies. Aus dem Browser nur vom Origin in `MYDOCS_MYLIFE_URL` (CORS).

| Aufruf | Antwort |
| --- | --- |
| `GET /api/mylife/export` | Der Stand unten |
| `GET /api/mylife/documents/{id}/content` | Inhalt der aktuellen Version eines markierten Dokuments (`application/octet-stream`); 404 für alles andere |
| `POST /api/mylife/synced` mit `{ "fingerprint": "…" }` und `X-MyDocs-Request: 1` | 204; quittiert den übernommenen Stand |

Falsches, widerrufenes oder gesperrtes Token: 401 mit `{ "title": "invalid-token" }`.

## Stand

```json
{
  "format": "mydocs-bridge",
  "version": 1,
  "generatedAt": "2026-11-28T10:00:00+00:00",
  "fingerprint": "64 Hex-Zeichen",
  "instance": { "url": "https://mydocs.eckis-corner.de/" },
  "person": { "id": "…", "name": "Bea" },
  "objects": [
    {
      "id": "…",
      "type": "insurance",
      "schemaVersion": 1,
      "title": "Haftpflicht",
      "fields": { "insuranceKind": "liability", "insurer": "HUK", "paymentAccount": "ID eines Eintrags in objects" },
      "locations": { "policyLocation": "ID eines Ablageorts in objects" },
      "expiresOn": "2027-12-31",
      "lifecycleState": "active",
      "lifecycleDate": null,
      "ended": false,
      "lastReviewedOn": "2026-10-01",
      "overdueSince": null,
      "updatedAt": "…",
      "documents": [
        { "id": "…", "hash": "64 Hex-Zeichen", "title": "Police 2026", "fileName": "police.pdf", "contentType": "application/pdf", "size": 123456, "date": "2026-01-15" }
      ]
    }
  ]
}
```

- **Umfang (163):** eigene private Einträge der Person und Haushaltseinträge, für die sie zuständig ist, jeweils mit „In myLife führen“; dazu die Ablageorte (Typ `location`), auf die sie verweisen.
- **Felder** wie im Katalog von [my-common](https://github.com/eckis-corner/my-common) in kanonischer Form (wie in [format.md von myLife](https://github.com/eckis-corner/myLife/blob/main/docs/format.md)). Verweisfelder zeigen nur auf Einträge in `objects`; andere sind entfernt. Ablageort-Felder enthalten den Namen, `locations` die ID.
- **Lebenszyklus:** `lifecycleState` ist einer der Zustände von myDocs (`draft`, `active`, `cancelled`, `ended`, `valid`, `expiring`, `expired`, `replaced`, `present`, `disposed`, `archived`) oder fehlt. `ended` ist `true` bei `ended`, `archived`, `expired`, `replaced`, `disposed`; myLife setzt dann den Status *beendet* (29).
- **`overdueSince`:** Die Prüfung in myDocs ist seit diesem Tag überfällig (14); myLife zeigt „Stand ungeprüft seit …“.
- **Dokumente (58):** nur markierte, höchstens 25 MB, die aktuelle Version. `hash` ändert sich mit dem Inhalt (HMAC-SHA-256 des Klartexts mit einem Schlüssel von myDocs, kein Prüfwert für den Inhalt).
- **Fingerabdruck (164):** SHA-256 (hex) über die JSON-Serialisierung von `objects` nach `id` sortiert, mit `updatedAt` auf dem Standardwert. Er ändert sich, wenn sich etwas Übertragenes ändert, nicht bei Notizen oder Vorgängen. myLife schickt ihn nach dem Übernehmen unverändert zurück; nachrechnen muss myLife ihn nicht.
