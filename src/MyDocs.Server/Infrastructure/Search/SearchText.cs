using System.Globalization;
using System.Text;
using MyCommon.Catalog;
using MyDocs.Server.Data;

namespace MyDocs.Server.Infrastructure.Search;

/// <summary>
/// Suchtext eines Objekts oder Dokuments (25): Titel, Art, Feldwerte und Notizen. Auswahlwerte stehen mit ihrer
/// deutschen und englischen Bezeichnung darin, IBANs auch gruppiert, Beträge auch mit Komma, damit die Suche nach dem
/// fragt, was Menschen sehen, nicht nach den gespeicherten Schlüsseln.
/// </summary>
internal static class SearchText
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

    public static string For(DocObject item)
    {
        var text = new StringBuilder();
        Add(text, item.Title);
        var type = ObjectCatalog.Find(item.TypeKey);
        if (type is not null)
        {
            Add(text, CatalogTexts.Find($"Type_{type.Key}", German));
            Add(text, CatalogTexts.Find($"Type_{type.Key}", English));
        }

        foreach (var (key, value) in item.Fields)
        {
            var field = type?.Field(key);
            switch (field?.Type)
            {
                case FieldType.Choice when field.OptionSet is not null:
                    Add(text, CatalogTexts.Find($"Option_{field.OptionSet}_{value}", German));
                    Add(text, CatalogTexts.Find($"Option_{field.OptionSet}_{value}", English));
                    break;
                case FieldType.Reference or FieldType.YesNo:
                    // Verweise sind IDs, Ja/Nein trägt nichts zur Suche bei.
                    break;
                case FieldType.Iban:
                    Add(text, value);
                    Add(text, FieldValues.GroupIban(value));
                    break;
                case FieldType.Money or FieldType.Number or FieldType.Percent when FieldValues.TryNumber(value, out var number):
                    Add(text, value);
                    Add(text, number.ToString("0.##", German));
                    break;
                default:
                    Add(text, value);
                    break;
            }
        }

        Add(text, item.Notes);
        return text.ToString().Trim();
    }

    public static string For(Document document, IEnumerable<string> fileNames)
    {
        var text = new StringBuilder();
        Add(text, document.Title);
        Add(text, document.Note);
        foreach (var name in fileNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Add(text, name);
        }

        return text.ToString().Trim();
    }

    private static void Add(StringBuilder text, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            text.Append(value.Trim()).Append('\n');
        }
    }
}

/// <summary>Funktion <c>word_similarity</c> aus <c>pg_trgm</c> für Abfragen in EF Core (Teilwörter und Tippfehler).</summary>
public static class SearchFunctions
{
    public static double WordSimilarity(string query, string text) => throw new InvalidOperationException("Nur in Abfragen.");
}
