# Abnahme von M1 auf der Synology

Checkliste für den Meilenstein M1 auf der DS1520+ ([meilensteine.md](../meilensteine.md)). Erst danach kommen echte Daten (67), und zwar nach M2. Zum Abhaken: `[ ]` durch `[x]` ersetzen. Wenn etwas nicht wie beschrieben funktioniert: notieren, was du getan hast und was passiert ist, dann weiter mit dem nächsten Punkt.

Gebraucht werden drei Konten mit eigenen Mail-Adressen: **A** (Administrator), **B** (Mitglied) und **C** (Gast). Die Adressen müssen nicht echt sein, Mails verschickt myDocs erst ab M2.

## 1. Installation

- [ ] Ordner, `.env` und Rechte wie in [installation.md](installation.md), Abschnitte 1–3
- [ ] `MYDOCS_BACKUP_KEY` erzeugt (`openssl rand -base64 32`), in die `.env` eingetragen, **ausgedruckt** und getrennt von der NAS abgelegt
- [ ] Projekt im Container Manager gestartet, `http://<IP>:8090/health/ready` liefert `Healthy`
- [ ] Nginx Proxy Manager mit `client_max_body_size 110m;`, `https://mydocs.eckis-corner.de` erreichbar, Zertifikat gültig
- [ ] myLife läuft daneben unverändert weiter
- [ ] Die Seite sieht aus wie myLife, nur mit Petrol statt Orange; Schrift Titillium Web; hell und dunkel umschaltbar; Deutsch und Englisch umschaltbar

## 2. Einrichtung und Anmeldung

- [ ] Einrichtungscode aus dem Protokoll von `mydocs-app-1`; ein falscher Code wird abgelehnt
- [ ] Konto A mit Passwort und Authenticator-App angelegt, die 10 Wiederherstellungscodes gesichert
- [ ] Nach einem Neustart des Containers steht kein Einrichtungscode mehr im Protokoll, `/setup` leitet zur Anmeldung
- [ ] Abmelden, neu anmelden: erst Passwort, dann Code. Derselbe Code ein zweites Mal wird abgelehnt
- [ ] Anmeldung mit einem Wiederherstellungscode klappt; derselbe Code klappt kein zweites Mal
- [ ] Fünfmal falsches Passwort: Konto für 15 Minuten gesperrt

## 3. Benutzer und Haushalt (als A)

- [ ] Verwaltung → Benutzer: B als Mitglied und C als Gast angelegt, Aktivierungslinks kopiert
- [ ] B und C aktivieren ihr Konto über den Link (im privaten Fenster des Browsers oder auf dem Smartphone); ein zweites Öffnen des Links sagt „ungültig“
- [ ] Haushalt: alle drei als Personen sichtbar; als A ein Kind ohne Konto mit Geburtsdatum hinzugefügt
- [ ] Als C: Haushalt nur lesbar, kein Knopf „Person hinzufügen“
- [ ] C gesperrt: Anmeldung von C sagt „gesperrt“; entsperrt: Anmeldung geht wieder
- [ ] Zugang von B zurückgesetzt: altes Passwort geht nicht mehr, neuer Link funktioniert
- [ ] Das eigene Konto lässt sich weder sperren noch herabstufen

## 4. Einträge (als A und B)

- [ ] Je Bereich mindestens ein Testeintrag, etwa Girokonto (IBAN), Haftpflicht, Stromvertrag, Ausweis mit Ablaufdatum
- [ ] Unter „Ablageorte“ zwei Orte; bei einem Schließfach den Ablageort des Schlüssels gewählt; das Datenblatt des Ablageorts zeigt „Verweise von“
- [ ] Pflichtfeld leer lassen: Hinweis direkt am Feld
- [ ] B ändert den Eintrag von A; die Änderungen erscheinen unter „Änderungen“ mit Name, Zeit, alt → neu
- [ ] A und B bearbeiten denselben Eintrag gleichzeitig: wer zuletzt speichert, bekommt den Hinweis auf die Änderung des anderen
- [ ] Als C: Einträge lesbar, kein Knopf „Bearbeiten“ und kein „Neu“
- [ ] B legt einen **privaten** Eintrag an: A und C sehen ihn nicht, auch nicht in der Suche
- [ ] B gibt ihn A zum Lesen frei: A sieht ihn, kann ihn nicht ändern; im Protokoll von B steht „Eintrag angesehen (Freigabe)“
- [ ] Löschen: Eintrag liegt im Papierkorb, lässt sich wiederherstellen

## 5. Dokumente

- [ ] PDF hochgeladen: Vorschau im Browser; Bild hochgeladen: Vorschau
- [ ] Word- oder Excel-Datei hochgeladen: nur Download, keine Vorschau
- [ ] Neue Version derselben Datei hochgeladen: „2 Versionen“, die alte lässt sich herunterladen
- [ ] Eine Datei mit 50–100 MB hochgeladen (prüft die Grenze im Nginx Proxy Manager)
- [ ] Per SSH: `ls /volume1/docker/mydocs/data/app/files/*/` zeigt nur Dateien mit langen Hex-Namen; `strings` auf eine Datei zeigt keinen lesbaren Inhalt

## 6. Suche

- [ ] Suche nach einem Teil der IBAN findet das Girokonto
- [ ] Suche nach „Privathaftpflicht“ findet die Haftpflicht, ein Tippfehler im Namen des Versicherers ebenfalls
- [ ] Suche nach einem Dateinamen findet das Dokument und führt zum Eintrag

## 7. Protokoll und Admin-Einsicht

- [ ] Benutzermenü → Protokoll (als B): eigene Aktionen und die Freigabe aus Teil 4
- [ ] Verwaltung → Gesamtes Protokoll (als A): Anmeldungen, Fehlversuche aus Teil 2, Benutzerverwaltung; IPs gekürzt
- [ ] A schaltet die Admin-Einsicht ein (Rückfrage, gelbes Band): Der private Eintrag von B ist sichtbar, aber nicht änderbar
- [ ] Im Protokoll von B steht „Admin-Einsicht“ mit dem Namen von A
- [ ] Neu laden: Admin-Einsicht ist aus

## 8. Sicherung und Rücksicherung

- [ ] Verwaltung → Sicherung: kein Hinweis „kein Sicherungsschlüssel“; „Jetzt sichern“, die Sicherung erscheint in der Liste und unter `data/app/backup`
- [ ] Hauptschlüssel mit Code angezeigt, **ausgedruckt** und getrennt von der NAS abgelegt; im Protokoll steht „Hauptschlüssel angezeigt“
- [ ] Am nächsten Morgen: Sicherung um 3 Uhr ist da
- [ ] **Rücksicherung geprobt:** einen Testeintrag löschen, Projekt stoppen, `restore … --confirm` wie in [installation.md](installation.md), Abschnitt 7, Projekt starten – der Eintrag und sein Dokument sind wieder da
- [ ] `/volume1/docker/mydocs` ist in Hyper Backup aufgenommen

## Ergebnis

- [ ] Alle Punkte erledigt oder die Abweichungen notiert und an Claude weitergegeben
