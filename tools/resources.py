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
