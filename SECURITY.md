# Sicherheitsrichtlinie

myDocs verwaltet die Unterlagen eines Haushalts. Sicherheitsmeldungen haben höchste Priorität.

## Lücken melden

Bitte melde Schwachstellen **vertraulich** über [GitHub Security Advisories](../../security/advisories/new) dieses Repositorys und **nicht** als öffentliches Issue. Hilfreich sind:

- betroffene Version oder Commit
- Beschreibung und Auswirkung
- Schritte zum Nachvollziehen

## Sicherheitsmodell in Kürze

- **Vertrauenswürdiger Server** (Entscheidung 2): Der Server kann Inhalte lesen, damit Suche, OCR und Vorschau funktionieren. Dateien und Datenbank liegen verschlüsselt auf dem Datenträger. Wer Zugriff auf die Synology und ihre Schlüssel hat, kommt an alles heran.
- Wofür Zero-Knowledge nötig ist, ist [myLife](https://github.com/eckis-corner/myLife) zuständig.
- Die Tests prüfen schon jetzt die strikte Content-Security-Policy (keine Inline-Skripte, `frame-ancestors 'none'`), `nosniff`, `no-referrer` und das Fehlen des `Server`-Headers. Mit M1.2 kommt der Test dazu, dass jeder Endpunkt unter `/api` eine Anmeldung verlangt.

## Unterstützte Versionen

Gepflegt wird der Stand von `main`. Vor Version 1.0 gibt es keine Zusage für echte Daten.
