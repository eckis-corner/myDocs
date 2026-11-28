using System.Text.Json;

namespace MyDocs.Desktop.Core;

/// <summary>Einstellungen des Clients; Geheimnisse liegen nicht hier, sondern im Schlüsselspeicher des Systems.</summary>
public sealed record DesktopSettings
{
    public string? ServerUrl { get; init; }

    public Guid? DeviceId { get; init; }

    /// <summary>Eingangsordner (M4.3): Dateien darin gehen in den eigenen Posteingang.</summary>
    public string? InboxFolder { get; init; }
}

/// <summary>Ordner des Clients: Einstellungen, ausgecheckte Dateien, Eingangsordner-Protokoll.</summary>
public sealed class DesktopPaths(string root)
{
    public static DesktopPaths ForCurrentUser() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "myDocs"));

    public string Root { get; } = root;

    public string Settings => Path.Combine(Root, "settings.json");

    public string Checkouts => Path.Combine(Root, "checkouts.json");

    /// <summary>Arbeitsordner der ausgecheckten Dateien (Klartext, nur für die eigene Person lesbar).</summary>
    public string Work => Path.Combine(Root, "work");

    /// <summary>Schreibgeschützt geöffnete Dateien; wird beim Start geleert.</summary>
    public string Open => Path.Combine(Root, "open");

    /// <summary>Legt einen Ordner an, auf Unix nur für den Besitzer (700).</summary>
    public static void EnsurePrivateDirectory(string path)
    {
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}

public sealed class DesktopSettingsStore(DesktopPaths paths)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly Lock _lock = new();

    public DesktopSettings Load()
    {
        lock (_lock)
        {
            try
            {
                return File.Exists(paths.Settings) ? JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(paths.Settings), Json) ?? new() : new();
            }
            catch (JsonException)
            {
                return new();
            }
        }
    }

    public DesktopSettings Update(Func<DesktopSettings, DesktopSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (_lock)
        {
            var next = change(Load());
            DesktopPaths.EnsurePrivateDirectory(paths.Root);
            var temporary = paths.Settings + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(next, Json));
            File.Move(temporary, paths.Settings, overwrite: true);
            return next;
        }
    }
}
