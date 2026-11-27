using MyCommon.Catalog;

namespace MyDocs.Shared.Objects;

/// <summary>
/// Was myDocs vom gemeinsamen Katalog nutzt (76). Die Bereiche nur für den Notfall (11 Persönliche Wünsche,
/// 12 Checkliste) bleiben in myLife (64), ebenso das eigene Datenblatt (<c>profile</c>): Personen führt myDocs selbst (77).
/// </summary>
public static class MyDocsCatalog
{
    public static IReadOnlyList<int> AreasOnlyInMyLife { get; } = [11, ObjectCatalog.ChecklistArea];

    public static IReadOnlyList<string> TypesOnlyInMyLife { get; } = ["profile"];

    public static IEnumerable<AreaDefinition> Areas =>
        ObjectCatalog.Areas.Where(a => a.IsAvailable && !AreasOnlyInMyLife.Contains(a.Number));

    public static IEnumerable<ObjectTypeDefinition> TypesIn(int area) =>
        ObjectCatalog.TypesIn(area).Where(IsAvailable);

    public static bool IsAvailable(ObjectTypeDefinition type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return !AreasOnlyInMyLife.Contains(type.Area) && !TypesOnlyInMyLife.Contains(type.Key);
    }

    /// <summary>Typ, den myDocs führt; null für unbekannte oder nur in myLife geführte Typen.</summary>
    public static ObjectTypeDefinition? Find(string? key) => ObjectCatalog.Find(key) is { } type && IsAvailable(type) ? type : null;

    /// <summary>Prüft Titel und Felder. Schlüssel: Feldschlüssel bzw. <c>title</c>.</summary>
    public static Dictionary<string, string> Validate(ObjectTypeDefinition type, ObjectData data)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(data);
        var errors = FieldValues.ValidateFields(type, data);
        if (string.IsNullOrWhiteSpace(data.Title))
        {
            errors["title"] = "required";
        }
        else if (data.Title.Length > 200)
        {
            errors["title"] = "too-long";
        }

        if (data.Notes is { Length: > FieldValues.MaxLongTextLength })
        {
            errors["notes"] = "too-long";
        }

        return errors;
    }
}
