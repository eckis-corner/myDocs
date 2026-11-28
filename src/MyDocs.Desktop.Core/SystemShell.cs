using System.Diagnostics;

namespace MyDocs.Desktop.Core;

/// <summary>Öffnet Dateien und Adressen mit der Standard-App des Systems.</summary>
public static class SystemShell
{
    public static void Open(string pathOrUrl)
    {
        var start = OperatingSystem.IsMacOS() ? new ProcessStartInfo("open") : OperatingSystem.IsLinux() ? new ProcessStartInfo("xdg-open") : new ProcessStartInfo(pathOrUrl) { UseShellExecute = true };
        if (!OperatingSystem.IsWindows())
        {
            start.ArgumentList.Add(pathOrUrl);
            start.UseShellExecute = false;
        }

        using var _ = Process.Start(start);
    }

    /// <summary>Name des Geräts für die Liste in der Web-App.</summary>
    public static string DeviceName()
    {
        var system = OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : "Linux";
        return $"{Environment.MachineName} ({system})";
    }
}
