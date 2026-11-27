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
    "AuthError_http-429": ("Zu viele Anfragen. Bitte warte eine Minute.", "Too many requests. Please wait a minute."),
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
