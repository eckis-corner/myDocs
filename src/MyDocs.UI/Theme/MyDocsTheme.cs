using MudBlazor;
using MyCommon.Ui.Theme;

namespace MyDocs.UI.Theme;

/// <summary>
/// Farbschema wie myLife (EckisTheme aus MyCommon.Ui, 55) mit der Akzentfarbe von myDocs: Petrol #1F9E8F (79).
/// Auf Petrol steht dunkle Schrift (Kontrast 6,1:1), als Schriftfarbe auf Weiß dient das dunklere #0F6B60 (6,4:1).
/// </summary>
public static class MyDocsTheme
{
    public static AppAccent Accent { get; } = new("#1f9e8f", "#04201c", "#0f6b60", "#9fe0d6");

    public static MudTheme Create() => EckisTheme.Create(Accent);
}
