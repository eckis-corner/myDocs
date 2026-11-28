# Abnahme von M1 bis M3 auf der Synology

Checkliste für die Meilensteine M1 (Teil 1–8), M2 (Teil 9–12) und M3 (ab Teil 13) auf der DS1520+ ([meilensteine.md](../meilensteine.md)). Erst danach kommen echte Daten (67). Zum Abhaken: `[ ]` durch `[x]` ersetzen. Wenn etwas nicht wie beschrieben funktioniert: notieren, was du getan hast und was passiert ist, dann weiter mit dem nächsten Punkt.

Gebraucht werden drei Konten mit eigenen Mail-Adressen: **A** (Administrator), **B** (Mitglied) und **C** (Gast). Für Teil 1–8 müssen die Adressen nicht echt sein; für die Abnahme von M2 (Teil 9–12) brauchen A und B echte Postfächer.

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
- [ ] Benutzermenü → Sicherheit: Passkey hinzugefügt (Laptop oder Smartphone); bei der nächsten Anmeldung nach dem Passwort „Mit Passkey bestätigen“; die Authenticator-App geht weiterhin

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
- [ ] Word- oder Excel-Datei hochgeladen: Der Download liefert die Originaldatei, die Vorschau ein PDF (ab M3)
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

## 9. Einstellungen und Mail (M2, als A)

- [ ] Verwaltung → Einstellungen: Adresse `https://mydocs.eckis-corner.de`, eigenes SMTP-Konto, **Speichern und Testmail an mich** kommt an
- [ ] Neu laden: beim Passwort steht „Hinterlegt“, das Passwort selbst ist nicht zu sehen
- [ ] Optional ntfy: Server eingetragen, Test an ein abonniertes Thema kommt in der App an
- [ ] Ein neues Konto D anlegen: Der Aktivierungslink kommt zusätzlich per Mail, der Link darin funktioniert
- [ ] Größte Datei auf 10 MB gesetzt: Eine Datei mit 20 MB wird abgelehnt; danach wieder 100 MB

## 10. Vorgänge und Wiedervorlage (M2)

- [ ] Ein Eintrag wird gekündigt (Lebenszyklus): Die Folgevorgänge erscheinen unter „Vorgänge“ und auf der Objektseite
- [ ] Bei einer Versicherung Hauptfälligkeit in zwei Monaten und Kündigungsfrist „1 Monat“ eingetragen: Spätestens nach 15 Minuten steht unter „Vorgänge“ die Frist „Kündigen oder verlängern?“
- [ ] Eine Prüfung mit Ergebnis abgeschlossen: „zuletzt geprüft“ am Eintrag ist heute
- [ ] Eine Prüfung zurückgestellt: nur mit Begründung, höchstens 30 Tage; die Begründung steht als Kommentar am Vorgang
- [ ] B kommentiert einen Vorgang von A; A sieht den Kommentar

## 11. Startseite (M2)

- [ ] Die Startseite zeigt fällige Vorgänge, Fristen, Zahlungen je Monat, Posteingang und Zustand des Bestands
- [ ] „Meins“ und „Haushalt“ zeigen unterschiedliche Zahlen, sobald B eigene Einträge hat
- [ ] Die Summe der Zahlungen stimmt mit den Beträgen der Verträge überein (jährlich ÷ 12 usw.)

## 12. Benachrichtigungen (M2, als A und B)

- [ ] Benutzermenü → Benachrichtigungen: B stellt die Zusammenfassung auf die nächste volle Stunde, **Speichern und testen** bringt eine Testmail
- [ ] Ein Vorgang von B mit Fälligkeit gestern: Zur gewählten Stunde kommt die Zusammenfassung, am selben Tag kein zweites Mal
- [ ] Ohne fällige Vorgänge kommt keine Zusammenfassung
- [ ] Mit ntfy: Die Push-Nachricht nennt nur Zahlen, keine Titel

## 13. Verarbeitung und Volltext (M3.1, M3.2)

- [ ] Verwaltung → Einstellungen → Verarbeitung: „Der Worker ist bereit.“, „0 fehlgeschlagen“
- [ ] Ein Brief, mit dem Handy fotografiert und als JPEG hochgeladen: kurz „Wird verarbeitet …“, dann Vorschaubild; die Vorschau ist ein PDF, gerade ausgerichtet
- [ ] Ein Scan als PDF ohne Text (vom Scanner): Die Suche nach einem Wort aus dem Brief findet das Dokument und zeigt die Stelle
- [ ] Eine Word- und eine Excel-Datei: Vorschaubild und Vorschau als PDF, der Download ist die Originaldatei
- [ ] Ein PDF mit Text (etwa eine Rechnung als Download): Suche nach der Rechnungsnummer findet es
- [ ] Suche nach einem Wort im Plural, das im Brief im Singular steht (etwa „Versicherungen“): wird gefunden
- [ ] B lädt ein Dokument an einen privaten Eintrag: A findet ein Wort daraus **nicht** (ohne Admin-Einsicht)
- [ ] Per SSH: `sudo docker exec mydocs-worker-1 ls /tmp` ist nach der Verarbeitung leer; `sudo docker exec mydocs-worker-1 python3 -c "import urllib.request; urllib.request.urlopen('https://example.com', timeout=5)"` scheitert (kein Internet)
- [ ] Worker stoppen, ein Dokument hochladen: Es wird gespeichert und bleibt „Wird verarbeitet …“; Worker starten: nach spätestens 10 Minuten fertig

## 14. Posteingang (M3.3, als A, B und C)

- [ ] B: Posteingang → **Hochladen** von drei Dateien: alle drei erscheinen als „Privat“; A sieht sie nicht, auch nicht in der Admin-Einsicht; C sieht im Menü keinen Posteingang
- [ ] B zuordnen: Titel ändern, Dokumentart „Rechnung“, Datum, zwei Einträge, „Vorgang dazu anlegen“ mit Fälligkeit; danach steht das Dokument an beiden Einträgen und der Vorgang unter „Vorgänge“ bei B
- [ ] B: **An den Haushalt geben**; A sieht es jetzt und kann **Selbst übernehmen**
- [ ] B: **Verwerfen**, unter „Verworfene“ **Zurückholen**
- [ ] Startseite: Der Block „Posteingang“ zählt bei „Meins“ nur den eigenen, bei „Haushalt“ auch den gemeinsamen; ein Klick auf einen Titel öffnet ihn im Posteingang
- [ ] Die Suche nach einem Wort aus einem Dokument im Posteingang findet es und führt in den Posteingang
- [ ] Ein Dokument eine Woche liegen lassen: Vorgang „Posteingang abarbeiten“ bei B, nach dem Zuordnen ist er weg

## 15. Kamera (M3.4, als B am Handy)

- [ ] Posteingang → **Kamera** → **Seite fotografieren**: Die Rückkamera öffnet sich; drei Seiten eines Briefs aufnehmen
- [ ] Eine Seite nach vorn schieben, eine drehen, eine an zwei Rändern zuschneiden: Die Vorschau zeigt es gleich
- [ ] **Als PDF in den Posteingang**: Das Dokument „Kamera …“ erscheint, nach der Verarbeitung mit Vorschaubild; die Vorschau zeigt die Seiten in der gewählten Reihenfolge, gerade ausgerichtet und zugeschnitten
- [ ] Die Suche nach einem Wort aus dem Brief findet das Dokument
- [ ] Am Computer: **Fotos wählen** mit mehreren Bildern auf einmal

## Ergebnis

- [ ] Alle Punkte erledigt oder die Abweichungen notiert und an Claude weitergegeben
