namespace MyDocs.UI;

/// <summary>
/// Bereiche des Katalogs, die myDocs zeigt (76). Die Bereiche nur für den Notfall (11 Persönliche Wünsche,
/// 12 Checkliste) bleiben in myLife (64).
/// </summary>
public static class Areas
{
    public static IReadOnlyList<int> OnlyInMyLife { get; } = [11, MyCommon.Catalog.ObjectCatalog.ChecklistArea];

    public static IEnumerable<MyCommon.Catalog.AreaDefinition> Visible =>
        MyCommon.Catalog.ObjectCatalog.Areas.Where(a => a.IsAvailable && !OnlyInMyLife.Contains(a.Number));
}
