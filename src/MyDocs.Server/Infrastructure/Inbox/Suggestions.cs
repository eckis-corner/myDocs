using MyCommon.Catalog;
using MyDocs.Server.Data;
using MyDocs.Shared.Documents;

namespace MyDocs.Server.Infrastructure.Inbox;

/// <summary>
/// Zuordnungsvorschläge (20, 175): Steht eine Policen-, Kunden-, Vertrags- oder Steuernummer, eine IBAN oder ein
/// Kennzeichen eines Eintrags im erkannten Text, wird der Eintrag vorgeschlagen. Verglichen wird ohne Leerzeichen,
/// Bindestriche und Groß-/Kleinschreibung. Nur Vorschläge: zugeordnet wird von Hand.
/// </summary>
internal static class Suggestions
{
    private const int MaxSuggestions = 3;

    /// <summary>Unter so vielen Zeichen ist ein Treffer Zufall (etwa „12345“ in jedem Brief).</summary>
    private const int MinLength = 6;

    /// <summary>Felder, die einen Eintrag eindeutig benennen: alle Text-Felder auf „…Number“ und ein paar weitere.</summary>
    private static readonly HashSet<string> ExtraKeys = new(StringComparer.Ordinal) { "licensePlate", "domainName", "username" };

    public static bool IsIdentifying(FieldDefinition field) =>
        field.Type == FieldType.Iban || (field.Type == FieldType.Text && (field.Key.EndsWith("Number", StringComparison.Ordinal) || ExtraKeys.Contains(field.Key)));

    public static IReadOnlyList<InboxSuggestionDto> For(string? text, IReadOnlyCollection<DocObject> objects)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var haystack = Normalize(text);
        var result = new List<(InboxSuggestionDto Suggestion, int Score)>();
        foreach (var item in objects)
        {
            if (ObjectCatalog.Find(item.TypeKey) is not { } type)
            {
                continue;
            }

            var matched = new List<string>();
            var score = 0;
            foreach (var field in type.Fields.Where(IsIdentifying))
            {
                if (item.Fields.TryGetValue(field.Key, out var value) && Normalize(value) is { Length: >= MinLength } needle && haystack.Contains(needle, StringComparison.Ordinal))
                {
                    matched.Add(field.Key);
                    score += field.Type == FieldType.Iban ? 3 : 2;
                }
            }

            if (matched.Count > 0)
            {
                result.Add((new InboxSuggestionDto(item.Id, item.Title, item.TypeKey, matched), score));
            }
        }

        return [.. result.OrderByDescending(r => r.Score).ThenBy(r => r.Suggestion.Title, StringComparer.CurrentCultureIgnoreCase).Take(MaxSuggestions).Select(r => r.Suggestion)];
    }

    internal static string Normalize(string value) => new([.. value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);
}
