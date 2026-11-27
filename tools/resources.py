#!/usr/bin/env python3
"""Erzeugt SharedResource.resx (Deutsch, neutral) und SharedResource.en.resx für MyDocs.UI aus einer Quelle,
damit beide Sprachen immer dieselben Schlüssel haben. Die Texte des Objektkatalogs kommen nicht von hier, sondern aus
MyCommon.Catalog (CatalogText, Entscheidung 71).

Aufruf: python3 tools/resources.py
"""
import re
from pathlib import Path
from xml.sax.saxutils import escape

TEXTS: dict[str, tuple[str, str]] = {
    "AppName": ("myDocs", "myDocs"),
    "NavHome": ("Übersicht", "Overview"),
    "OpenMenu": ("Menü öffnen", "Open menu"),
    "ToggleDarkMode": ("Hell/Dunkel umschalten", "Toggle light/dark mode"),
    "SkipToContent": ("Zum Inhalt springen", "Skip to content"),
    "Language": ("Sprache", "Language"),
    "LanguageName_de": ("Deutsch", "Deutsch"),
    "LanguageName_en": ("English", "English"),
    "Loading": ("Wird geladen …", "Loading …"),
    "HomeTitle": ("Willkommen bei myDocs", "Welcome to myDocs"),
    "HomeIntro": (
        "myDocs verwaltet die Unterlagen eures Haushalts: Versicherungen, Konten, Verträge und Abos als eigene Einträge "
        "mit ihren Dokumenten, mit Wiedervorlage und Zuständigkeit. Was im Ernstfall gebraucht wird, übernimmt myLife.",
        "myDocs manages your household's records: insurance policies, accounts, contracts and subscriptions as entries of "
        "their own with their documents, follow-ups and a person in charge. What is needed in an emergency goes to myLife.",
    ),
    "HomeAreas": ("Bereiche", "Areas"),
    "HomeStatusUnreachable": (
        "Der Server ist nicht erreichbar. Bitte versuche es später erneut.",
        "The server cannot be reached. Please try again later.",
    ),
    "HomeStatusVersion": ("Version", "Version"),
    "NotFoundTitle": ("Seite nicht gefunden", "Page not found"),
    "NotFoundText": ("Die aufgerufene Seite gibt es nicht.", "The page you requested does not exist."),
    "BackToHome": ("Zur Übersicht", "Back to overview"),
    "ErrorUnhandled": ("Ein unerwarteter Fehler ist aufgetreten.", "An unexpected error has occurred."),
    "Reload": ("Neu laden", "Reload"),
    # Konto, Anmeldung (M1.2)
    "UserMenu": ("Konto", "Account"),
    "Logout": ("Abmelden", "Log out"),
    "Email": ("E-Mail-Adresse", "Email address"),
    "Password": ("Passwort", "Password"),
    "PasswordHelp": ("Mindestens {0} Zeichen. Ein Satz aus mehreren Wörtern ist leicht zu merken und sicher.",
                     "At least {0} characters. A sentence of several words is easy to remember and secure."),
    "DisplayName": ("Name", "Name"),
    "TotpCode": ("Code aus der Authenticator-App", "Code from the authenticator app"),
    "RecoveryCode": ("Wiederherstellungscode", "Recovery code"),
    "LoginTitle": ("Anmelden", "Log in"),
    "LoginNext": ("Weiter", "Next"),
    "LoginSubmit": ("Anmelden", "Log in"),
    "LoginTotpText": ("Gib den sechsstelligen Code aus deiner Authenticator-App ein.", "Enter the six-digit code from your authenticator app."),
    "LoginRecoveryText": ("Gib einen deiner Wiederherstellungscodes ein. Jeder Code gilt nur einmal.",
                          "Enter one of your recovery codes. Each code works only once."),
    "LoginUseRecovery": ("Kein Zugriff auf die App? Wiederherstellungscode verwenden", "No access to the app? Use a recovery code"),
    "LoginUseTotp": ("Code aus der Authenticator-App verwenden", "Use a code from the authenticator app"),
    # Einrichtung (M1.2)
    "SetupTitle": ("Einrichtung", "Setup"),
    "SetupStepOf": ("Schritt {0} von {1}", "Step {0} of {1}"),
    "SetupIntro": (
        "myDocs ist noch nicht eingerichtet. Du legst jetzt das erste Konto an, es wird Administrator. Weitere Konten "
        "legst du danach in der Verwaltung an.",
        "myDocs has not been set up yet. You are about to create the first account; it becomes the administrator. You "
        "create further accounts afterwards in the administration.",
    ),
    "SetupCode": ("Einrichtungscode", "Setup code"),
    "SetupCodeHelp": (
        "Steht im Protokoll des Containers, etwa im Container Manager unter mydocs-app → Protokoll. Nach jedem Neustart gilt ein neuer Code.",
        "Shown in the container log, for example in Container Manager under mydocs-app → Log. After every restart a new code applies.",
    ),
    "SetupNext": ("Weiter", "Next"),
    "SetupTotpText": (
        "Scanne den QR-Code mit einer Authenticator-App (etwa Microsoft Authenticator, Aegis oder 1Password) und gib den angezeigten Code ein.",
        "Scan the QR code with an authenticator app (for example Microsoft Authenticator, Aegis or 1Password) and enter the code it shows.",
    ),
    "SetupTotpQr": ("QR-Code für die Authenticator-App", "QR code for the authenticator app"),
    "SetupTotpManual": ("Ohne Kamera diesen Schlüssel eintragen:", "Without a camera, enter this key:"),
    "SetupFinish": ("Konto anlegen", "Create account"),
    "SetupRecoveryText": (
        "Das sind deine Wiederherstellungscodes. Mit ihnen kommst du in dein Konto, wenn das Gerät mit der Authenticator-App "
        "verloren geht. Drucke sie aus oder speichere sie im Passwortmanager. Sie werden nicht noch einmal angezeigt.",
        "These are your recovery codes. They get you into your account if you lose the device with the authenticator app. "
        "Print them or store them in your password manager. They will not be shown again.",
    ),
    "SetupRecoverySaved": ("Ich habe die Codes sicher aufbewahrt.", "I have stored the codes safely."),
    "SetupDone": ("Zu myDocs", "Go to myDocs"),
    # Fehler der Anmeldung und Einrichtung
    "AuthError_auth-invalid": ("E-Mail-Adresse oder Passwort stimmen nicht.", "Email address or password is incorrect."),
    "AuthError_auth-locked": ("Zu viele Fehlversuche. Das Konto ist für 15 Minuten gesperrt.", "Too many failed attempts. The account is locked for 15 minutes."),
    "AuthError_auth-code-invalid": ("Der Code stimmt nicht oder wurde schon verwendet.", "The code is incorrect or has already been used."),
    "AuthError_auth-2fa-missing": ("Für dieses Konto ist kein zweiter Faktor eingerichtet. Bitte wende dich an den Administrator.",
                                   "No second factor is set up for this account. Please contact the administrator."),
    "AuthError_setup-done": ("myDocs ist bereits eingerichtet.", "myDocs has already been set up."),
    "AuthError_setup-code-invalid": ("Der Einrichtungscode stimmt nicht.", "The setup code is incorrect."),
    "AuthError_password-weak": ("Das Passwort ist zu kurz.", "The password is too short."),
    "AuthError_email-invalid": ("Die E-Mail-Adresse ist ungültig.", "The email address is invalid."),
    "AuthError_server-unreachable": ("Der Server ist nicht erreichbar. Bitte versuche es später erneut.", "The server cannot be reached. Please try again later."),
    "AuthError_auth-disabled": ("Dieses Konto ist gesperrt. Bitte wende dich an den Administrator.", "This account is locked. Please contact the administrator."),
    "AuthError_activation-invalid": (
        "Der Link ist ungültig, abgelaufen oder schon benutzt. Bitte den Administrator um einen neuen.",
        "The link is invalid, has expired or has already been used. Please ask the administrator for a new one.",
    ),
    "AuthError_http-429": ("Zu viele Anfragen. Bitte warte eine Minute.", "Too many requests. Please wait a minute."),
    # Allgemein
    "Yes": ("Ja", "Yes"),
    "No": ("Nein", "No"),
    "Save": ("Speichern", "Save"),
    "Cancel": ("Abbrechen", "Cancel"),
    "Close": ("Schließen", "Close"),
    "Edit": ("Bearbeiten", "Edit"),
    "Delete": ("Löschen", "Delete"),
    "Confirm": ("Bestätigen", "Confirm"),
    "Actions": ("Aktionen", "Actions"),
    "Note": ("Notiz", "Note"),
    "Copied": ("In die Zwischenablage kopiert.", "Copied to the clipboard."),
    "CopyLink": ("Link kopieren", "Copy link"),
    "NavAdmin": ("Verwaltung", "Administration"),
    # Aktivierung (M1.3)
    "ActivateTitle": ("Konto aktivieren", "Activate account"),
    "ActivateIntro": (
        "Hallo {0}, der Administrator hat ein Konto für dich angelegt. Lege jetzt dein Passwort fest und richte danach die Authenticator-App ein.",
        "Hello {0}, the administrator has created an account for you. Choose your password now and set up the authenticator app afterwards.",
    ),
    "ActivateFinish": ("Konto aktivieren", "Activate account"),
    # Benutzer (M1.3)
    "UsersTitle": ("Benutzer", "Users"),
    "UsersIntro": (
        "Nur du als Administrator legst Konten an. Jedes neue Konto bekommt einen Aktivierungslink, den du selbst weitergibst, etwa per Messenger. "
        "Er gilt einmal und sieben Tage.",
        "Only you as administrator create accounts. Every new account gets an activation link that you pass on yourself, for example by messenger. "
        "It works once and for seven days.",
    ),
    "UserCreate": ("Konto anlegen", "Create account"),
    "HouseholdRole": ("Rolle im Haushalt", "Role in the household"),
    "HouseholdRoleHelp": ("Mitglieder lesen und ändern den gemeinsamen Bestand, Gäste lesen nur.", "Members read and change the shared records, guests only read."),
    "Role_member": ("Mitglied", "Member"),
    "Role_guest": ("Gast", "Guest"),
    "IsAdmin": ("Administrator", "Administrator"),
    "UserState": ("Zustand", "State"),
    "UserState_pending": ("wartet auf Aktivierung", "awaiting activation"),
    "UserState_active": ("aktiv", "active"),
    "UserState_locked": ("gesperrt", "locked"),
    "UserLock": ("Sperren", "Lock"),
    "UserUnlock": ("Entsperren", "Unlock"),
    "UserLockConfirm": ("{0} sperren? Laufende Sitzungen enden innerhalb einer Minute.", "Lock {0}? Running sessions end within a minute."),
    "UserReset": ("Zugang zurücksetzen", "Reset access"),
    "UserResetConfirm": (
        "Passwort und zweiter Faktor von {0} werden gelöscht, alle Sitzungen enden. Danach gibt es einen neuen Aktivierungslink.",
        "The password and second factor of {0} are deleted and all sessions end. Afterwards there is a new activation link.",
    ),
    "ActivationLinkTitle": ("Aktivierungslink", "Activation link"),
    "ActivationLinkText": (
        "Gib diesen Link an {0} weiter. Er gilt einmal und bis {1}. Wer ihn hat, kann das Konto aktivieren, also nur auf sicherem Weg verschicken.",
        "Pass this link on to {0}. It works once and until {1}. Whoever has it can activate the account, so send it only through a safe channel.",
    ),
    # Haushalt und Personen (M1.3)
    "HouseholdTitle": ("Haushalt", "Household"),
    "HouseholdIntro": (
        "Alle Personen des Haushalts: wer sich anmelden kann und wer ohne eigenes Konto vorkommt, etwa die Kinder als versicherte Personen.",
        "Everyone in the household: who can log in and who appears without an account of their own, for example the children as insured persons.",
    ),
    "PersonCreate": ("Person hinzufügen", "Add person"),
    "PersonLogin": ("Konto", "Account"),
    "PersonNameFromAccount": ("Der Name kommt aus dem Konto; ändern kann ihn der Administrator.", "The name comes from the account; the administrator can change it."),
    "PersonDeleteConfirm": ("{0} entfernen?", "Remove {0}?"),
    "BirthDate": ("Geburtsdatum", "Date of birth"),
    "HouseholdError_name-required": ("Bitte einen Namen eingeben.", "Please enter a name."),
    "HouseholdError_person-has-user": ("Personen mit Konto lassen sich nicht entfernen.", "People with an account cannot be removed."),
    "HouseholdError_not-found": ("Nicht gefunden.", "Not found."),
    "HouseholdError_email-taken": ("Für diese E-Mail-Adresse gibt es schon ein Konto.", "There is already an account for this email address."),
    "HouseholdError_email-invalid": ("Die E-Mail-Adresse ist ungültig.", "The email address is invalid."),
    "HouseholdError_role-invalid": ("Diese Rolle gibt es nicht. Administratoren sind immer Mitglied.", "This role does not exist. Administrators are always members."),
    "HouseholdError_last-admin": ("Das ist der letzte aktive Administrator.", "This is the last active administrator."),
    "HouseholdError_not-self": ("Das geht nicht beim eigenen Konto.", "This is not possible for your own account."),
    "HouseholdError_auth-forbidden": ("Dafür fehlt die Berechtigung.", "You do not have permission for this."),
    # Objekte (M1.4)
    "NavAreas": ("Bereiche", "Areas"),
    "AreaNotAvailable": ("Diesen Bereich führt myDocs nicht.", "myDocs does not keep this area."),
    "AreaEmpty": ("Noch keine Einträge.", "No entries yet."),
    "Filter": ("Filtern", "Filter"),
    "RowsPerPage": ("Zeilen je Seite", "Rows per page"),
    "New": ("Neu", "New"),
    "NewObject": ("Neu: {0}", "New: {0}"),
    "ObjectTitle": ("Titel", "Title"),
    "ObjectType": ("Art", "Type"),
    "ObjectSummary": ("Angaben", "Details"),
    "ObjectCommon": ("Allgemein", "General"),
    "ObjectOwner": ("Gehört", "Belongs to"),
    "ObjectOwnerHelp": (
        "Haushalt: alle sehen den Eintrag, Mitglieder ändern ihn. Privat: nur du und wem du ihn freigibst.",
        "Household: everyone sees the entry, members change it. Private: only you and whoever you share it with.",
    ),
    "OwnerHousehold": ("Haushalt", "Household"),
    "OwnerMe": ("Mir (privat)", "Me (private)"),
    "ObjectResponsible": ("Zuständig", "Responsible"),
    "ObjectLinkedFrom": ("Verweise von", "Referenced by"),
    "Notes": ("Notizen", "Notes"),
    "Expiry_default": ("Ablaufdatum", "Expiry date"),
    "LocationNoneYet": ("Noch keine Ablageorte. Lege sie im Bereich „Ablageorte“ an.", "No storage places yet. Create them in the area “Storage places”."),
    "ObjectSaved": ("Gespeichert.", "Saved."),
    "ObjectDeleted": ("In den Papierkorb gelegt.", "Moved to the trash."),
    "ObjectRestored": ("Wiederhergestellt.", "Restored."),
    "ObjectDeleteConfirm": ("„{0}“ in den Papierkorb legen?", "Move “{0}” to the trash?"),
    "InTrash": ("Im Papierkorb", "In the trash"),
    "Restore": ("Wiederherstellen", "Restore"),
    "SharesTitle": ("Freigaben", "Sharing"),
    "SharesIntro": ("Dieser Eintrag ist privat. Gib ihn einzelnen Personen zum Lesen oder Bearbeiten frei.", "This entry is private. Share it with individual people to read or edit."),
    "ShareNone": ("Keine Freigabe", "Not shared"),
    "ShareRead": ("Darf lesen", "Can read"),
    "ShareEdit": ("Darf bearbeiten", "Can edit"),
    "HistoryTitle": ("Änderungen", "Changes"),
    "TrashTitle": ("Papierkorb", "Trash"),
    "TrashIntro": ("Gelöschte Einträge lassen sich hier wiederherstellen.", "Deleted entries can be restored here."),
    "TrashEmpty": ("Der Papierkorb ist leer.", "The trash is empty."),
    "ObjectError_type-unknown": ("Diese Art von Eintrag gibt es nicht.", "This kind of entry does not exist."),
    "ObjectError_type-only-in-mylife": ("Diese Art von Eintrag führt myLife.", "This kind of entry is kept in myLife."),
    "ObjectError_invalid": ("Bitte die markierten Felder prüfen.", "Please check the highlighted fields."),
    "ObjectError_not-found": ("Diesen Eintrag gibt es nicht oder du darfst ihn nicht sehen.", "This entry does not exist or you may not see it."),
    "ObjectError_forbidden": ("Dafür fehlt die Berechtigung.", "You do not have permission for this."),
    "ObjectError_conflict": (
        "Jemand anderes hat den Eintrag inzwischen geändert. Bitte neu laden und die Änderung noch einmal machen.",
        "Someone else has changed the entry in the meantime. Please reload and make your change again.",
    ),
    "ObjectError_owner-invalid": ("So lässt sich das Eigentum nicht ändern.", "Ownership cannot be changed this way."),
    "ObjectError_http-404": ("Nicht gefunden.", "Not found."),
    # Dokumente (M1.5)
    "DocumentsTitle": ("Dokumente", "Documents"),
    "DocumentsEmpty": ("Noch keine Dokumente. Lade Policen, Verträge, Schreiben oder Fotos hoch, je bis {0} MB.",
                       "No documents yet. Upload policies, contracts, letters or photos, up to {0} MB each."),
    "DocumentUpload": ("Hochladen", "Upload"),
    "DocumentNewVersion": ("Neue Version hochladen", "Upload new version"),
    "DocumentVersionAdded": ("Neue Version gespeichert.", "New version saved."),
    "DocumentVersions": ("{0} Versionen", "{0} versions"),
    "DocumentVersionDownload": ("Version {0} vom {1} ({2})", "Version {0} of {1} ({2})"),
    "DocumentTitle": ("Bezeichnung", "Title"),
    "DocumentDate": ("Datum des Dokuments", "Document date"),
    "DocumentDeleteConfirm": ("„{0}“ löschen? Alle Versionen kommen in den Papierkorb.", "Delete “{0}”? All versions go to the trash."),
    "Download": ("Herunterladen", "Download"),
    "DocumentError_too-large": ("Die Datei ist zu groß (höchstens {1} MB).", "The file is too large (at most {1} MB)."),
    "DocumentError_no-file": ("Keine Datei erhalten.", "No file received."),
    "DocumentError_not-found": ("Dieses Dokument gibt es nicht oder du darfst es nicht sehen.", "This document does not exist or you may not see it."),
    "DocumentError_forbidden": ("Dafür fehlt die Berechtigung.", "You do not have permission for this."),
    "DocumentError_needs-object": ("Ein Dokument gehört zu mindestens einem Eintrag.", "A document belongs to at least one entry."),
    "DocumentError_title-required": ("Bitte eine Bezeichnung eingeben.", "Please enter a title."),
    "DocumentError_http-413": ("Die Datei ist zu groß.", "The file is too large."),
    # Suche (M1.6)
    "SearchTitle": ("Suche", "Search"),
    "SearchPlaceholder": ("Suchen …", "Search …"),
    "SearchHelp": (
        "Sucht in Titeln, allen Feldern, Notizen und Dokumentnamen, auch nach Teilen von Nummern oder mit Tippfehlern.",
        "Searches titles, all fields, notes and document names, also for parts of numbers or with typos.",
    ),
    "SearchHits": ("{0} Treffer", "{0} results"),
    "SearchNoHits": ("Keine Treffer für „{0}“.", "No results for “{0}”."),
    "SearchDocumentIn": ("Dokument bei {0}", "Document of {0}"),
    # Protokoll und Admin-Einsicht (M1.7)
    "AuditTitle": ("Protokoll", "Log"),
    "AuditIntro": (
        "Was du getan hast und was andere mit deinen privaten Einträgen und Dokumenten getan haben, auch jede Einsicht durch den Administrator.",
        "What you did and what others did with your private entries and documents, including every look by the administrator.",
    ),
    "AdminAuditTitle": ("Gesamtes Protokoll", "Full log"),
    "AdminAuditIntro": ("Alle Ereignisse der Instanz. IP-Adressen sind gekürzt.", "All events of this instance. IP addresses are shortened."),
    "AuditAt": ("Zeitpunkt", "Time"),
    "AuditActor": ("Wer", "Who"),
    "AuditAction": ("Was", "What"),
    "AuditSubject": ("Betrifft", "Concerns"),
    "AuditSystem": ("System", "System"),
    "AuditEmpty": ("Noch keine Einträge.", "No entries yet."),
    "Audit_login": ("Angemeldet", "Logged in"),
    "Audit_login-failed": ("Anmeldung fehlgeschlagen", "Login failed"),
    "Audit_logout": ("Abgemeldet", "Logged out"),
    "Audit_setup-completed": ("myDocs eingerichtet", "myDocs set up"),
    "Audit_activated": ("Konto aktiviert", "Account activated"),
    "Audit_user-created": ("Konto angelegt", "Account created"),
    "Audit_user-changed": ("Konto geändert", "Account changed"),
    "Audit_user-locked": ("Konto gesperrt", "Account locked"),
    "Audit_user-unlocked": ("Konto entsperrt", "Account unlocked"),
    "Audit_user-reset": ("Zugang zurückgesetzt", "Access reset"),
    "Audit_object-created": ("Eintrag angelegt", "Entry created"),
    "Audit_object-changed": ("Eintrag geändert", "Entry changed"),
    "Audit_object-deleted": ("Eintrag gelöscht", "Entry deleted"),
    "Audit_object-restored": ("Eintrag wiederhergestellt", "Entry restored"),
    "Audit_object-shared": ("Freigaben geändert", "Sharing changed"),
    "Audit_object-viewed": ("Eintrag angesehen (Freigabe)", "Entry viewed (shared)"),
    "Audit_document-uploaded": ("Dokument hochgeladen", "Document uploaded"),
    "Audit_document-version-added": ("Neue Version hochgeladen", "New version uploaded"),
    "Audit_document-downloaded": ("Dokument geöffnet", "Document opened"),
    "Audit_document-deleted": ("Dokument gelöscht", "Document deleted"),
    "Audit_document-restored": ("Dokument wiederhergestellt", "Document restored"),
    "Audit_admin-view": ("Admin-Einsicht", "Admin view"),
    "Audit_trash-purged": ("Papierkorb geleert", "Trash emptied"),
    "AdminViewOn": ("Admin-Einsicht einschalten", "Turn on admin view"),
    "AdminViewOff": ("Admin-Einsicht ausschalten", "Turn off admin view"),
    "AdminViewConfirm": (
        "Mit der Admin-Einsicht siehst du auch die privaten Einträge der anderen, nur zum Lesen. Jeder Zugriff erscheint im Protokoll der Person, der der Eintrag gehört.",
        "With the admin view you also see the private entries of others, read-only. Every access appears in the log of the person the entry belongs to.",
    ),
    "AdminViewBanner": (
        "Admin-Einsicht aktiv: Du siehst auch private Einträge anderer. Jeder Zugriff wird den Eigentümern angezeigt.",
        "Admin view active: you also see private entries of others. Every access is shown to the owners.",
    ),
    # Sicherung und Hauptschlüssel (M1.7b)
    "BackupTitle": ("Sicherung", "Backup"),
    "BackupDaily": ("Tägliche Sicherung", "Daily backup"),
    "BackupIntro": (
        "myDocs sichert jeden Tag Datenbank, Dateien und Schlüssel verschlüsselt in data/app/backup. Das Synology-Backup übernimmt den Ordner von dort.",
        "myDocs backs up the database, files and keys every day, encrypted, to data/app/backup. Synology backup takes the folder from there.",
    ),
    "BackupNotConfigured": (
        "Es ist kein Sicherungsschlüssel eingerichtet (MYDOCS_BACKUP_KEY in der .env). Ohne ihn gibt es keine automatische Sicherung.",
        "No backup key is configured (MYDOCS_BACKUP_KEY in .env). Without it there is no automatic backup.",
    ),
    "BackupLast": ("Letzte Sicherung", "Last backup"),
    "BackupNext": ("Nächste", "Next"),
    "BackupNever": ("noch keine", "none yet"),
    "BackupNow": ("Jetzt sichern", "Back up now"),
    "BackupDone": ("Sicherung erstellt.", "Backup created."),
    "BackupFailed": ("Die Sicherung ist fehlgeschlagen. Details stehen im Protokoll des Containers.", "The backup failed. Details are in the container log."),
    "BackupLastError": ("Letzter Fehler am {0}: {1}", "Last error on {0}: {1}"),
    "MasterKeyTitle": ("Hauptschlüssel", "Master key"),
    "MasterKeyIntro": (
        "Mit dem Hauptschlüssel sind alle Dokumente verschlüsselt. Ohne ihn lassen sie sich nicht mehr öffnen, auch nicht aus einer Sicherung. "
        "Drucke ihn als 24 Wörter aus und bewahre ihn getrennt von der Synology auf, etwa beim Notfallordner. Zum Anzeigen brauchst du einen Code aus deiner Authenticator-App.",
        "All documents are encrypted with the master key. Without it they can no longer be opened, not even from a backup. "
        "Print it as 24 words and keep it away from the Synology, for example with your emergency folder. To show it you need a code from your authenticator app.",
    ),
    "MasterKeyShow": ("Hauptschlüssel anzeigen", "Show master key"),
    "MasterKeyHide": ("Ausblenden", "Hide"),
    "MasterKeyWarning": (
        "Wer diese Wörter und die Dateien hat, kann alle Dokumente lesen. Nicht abfotografieren, nicht per Mail verschicken. Die Anzeige steht im Protokoll.",
        "Whoever has these words and the files can read all documents. Do not photograph them or send them by email. Showing them is recorded in the log.",
    ),
    "MasterKeyRestoreHint": (
        "Wiederherstellen: docker compose run --rm app master-key restore \"wort1 … wort24\" --confirm (siehe Installationsanleitung).",
        "Restore: docker compose run --rm app master-key restore \"word1 … word24\" --confirm (see installation guide).",
    ),
    "Print": ("Drucken", "Print"),
    "Audit_backup-created": ("Sicherung erstellt", "Backup created"),
    "Audit_backup-failed": ("Sicherung fehlgeschlagen", "Backup failed"),
    "Audit_master-key-shown": ("Hauptschlüssel angezeigt", "Master key shown"),
    # Passkeys und Sicherheit (M1.2b)
    "SecurityTitle": ("Sicherheit", "Security"),
    "PasskeysTitle": ("Passkeys", "Passkeys"),
    "PasskeysIntro": (
        "Ein Passkey ersetzt nach dem Passwort den Code der Authenticator-App: Fingerabdruck, Gesicht oder PIN des Geräts genügen. Die Authenticator-App bleibt als Rückfall.",
        "After the password, a passkey replaces the code from the authenticator app: fingerprint, face or device PIN is enough. The authenticator app remains as a fallback.",
    ),
    "PasskeysNone": ("Noch kein Passkey.", "No passkey yet."),
    "PasskeyName": ("Bezeichnung", "Name"),
    "PasskeyNameExample": ("Mein Gerät", "My device"),
    "PasskeyAdd": ("Passkey hinzufügen", "Add passkey"),
    "PasskeyAdded": ("Passkey gespeichert.", "Passkey saved."),
    "PasskeyCancelled": ("Abgebrochen.", "Cancelled."),
    "PasskeyRemoveConfirm": ("Passkey „{0}“ entfernen?", "Remove passkey “{0}”?"),
    "PasskeyUnsupported": ("Dieser Browser kann keine Passkeys.", "This browser does not support passkeys."),
    "LoginUsePasskey": ("Mit Passkey bestätigen", "Confirm with passkey"),
    "RecoveryCodesTitle": ("Wiederherstellungscodes", "Recovery codes"),
    "RecoveryCodesLeft": ("Noch {0} von 10 Wiederherstellungscodes übrig.", "{0} of 10 recovery codes left."),
    "AuthError_passkey-invalid": ("Der Passkey wurde nicht angenommen.", "The passkey was not accepted."),
    "AuthError_passkey-cancelled": ("Die Bestätigung mit dem Passkey wurde abgebrochen.", "Confirming with the passkey was cancelled."),
    "Audit_passkey-added": ("Passkey hinzugefügt", "Passkey added"),
    "Audit_passkey-removed": ("Passkey entfernt", "Passkey removed"),
}

HEADER = """<?xml version="1.0" encoding="utf-8"?>
<!-- Automatisch erzeugt mit tools/resources.py. Nicht von Hand bearbeiten. -->
<root>
  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
  <resheader name="version"><value>2.0</value></resheader>
  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
"""


def english_quotes(text: str) -> str:
    """Englisch einheitlich mit typografischen Anführungszeichen; die Quellen schreiben oft gerade."""
    return re.sub(r'"([^"\n]*)"', "“\\1”", text)


def write(path: Path, index: int, texts: dict[str, tuple[str, str]] = TEXTS) -> None:
    lines = [HEADER]
    for key in sorted(texts):
        text = texts[key][index]
        value = escape(english_quotes(text) if index == 1 else text)
        lines.append(f'  <data name="{key}" xml:space="preserve"><value>{value}</value></data>\n')
    lines.append("</root>\n")
    path.write_text("".join(lines), encoding="utf-8")


if __name__ == "__main__":
    target = Path(__file__).resolve().parent.parent / "src" / "MyDocs.UI" / "Resources"
    write(target / "SharedResource.resx", 0)
    write(target / "SharedResource.en.resx", 1)
    print(f"{len(TEXTS)} Texte geschrieben nach {target}")
