# Sicherheitsrichtlinie

myDocs verwaltet die Unterlagen eines Haushalts. Sicherheitsmeldungen haben höchste Priorität.

## Lücken melden

Bitte melde Schwachstellen **vertraulich** über [GitHub Security Advisories](../../security/advisories/new) dieses Repositorys und **nicht** als öffentliches Issue. Hilfreich sind:

- betroffene Version oder Commit
- Beschreibung und Auswirkung
- Schritte zum Nachvollziehen

## Sicherheitsmodell in Kürze

- **Vertrauenswürdiger Server** (Entscheidung 2): Der Server kann Inhalte lesen, damit Suche, OCR und Vorschau funktionieren. Dokumente werden ab M1 verschlüsselt abgelegt (22), Geheimnisse der Anmeldung liegen mit Data Protection verschlüsselt in der Datenbank (82). Die Datenbank selbst ist nicht verschlüsselt; dafür bietet sich die Verschlüsselung des Synology-Volumes an. Wer Zugriff auf die Synology und ihre Schlüssel hat, kommt an alles heran.
- Wofür Zero-Knowledge nötig ist, ist [myLife](https://github.com/eckis-corner/myLife) zuständig.
- Die Tests prüfen schon jetzt die strikte Content-Security-Policy (keine Inline-Skripte, `frame-ancestors 'none'`), `nosniff`, `no-referrer` und das Fehlen des `Server`-Headers. Jeder Endpunkt unter `/api` verlangt eine Anmeldung, außer einer fest im Test hinterlegten Liste (Info, Einrichtung, Anmeldung); ein neuer Endpunkt ohne Schutz lässt den Test scheitern.
- Anmeldung: zweiter Faktor Pflicht, jeder TOTP-Code und jeder Wiederherstellungscode nur einmal, Sperre nach Fehlversuchen, Schreibzugriffe nur mit dem Header `X-MyDocs-Request`.

## Unterstützte Versionen

Gepflegt wird der Stand von `main`. Vor Version 1.0 gibt es keine Zusage für echte Daten.
