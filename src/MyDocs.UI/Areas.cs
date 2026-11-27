namespace MyDocs.UI;

/// <summary>Bereiche, die myDocs zeigt (76); Grundlage ist <see cref="MyDocs.Shared.Objects.MyDocsCatalog"/>.</summary>
public static class Areas
{
    public static IEnumerable<MyCommon.Catalog.AreaDefinition> Visible => MyDocs.Shared.Objects.MyDocsCatalog.Areas;
}
