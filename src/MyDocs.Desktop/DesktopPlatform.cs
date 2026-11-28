using System.Text.RegularExpressions;
using MyDocs.Desktop.Core;
using Microsoft.Extensions.Localization;
using MyDocs.Desktop.Core.Auth;
using MyDocs.Desktop.Core.Checkout;
using MyDocs.UI.Resources;
using MyDocs.UI.Services;

namespace MyDocs.Desktop;

/// <summary>
/// Was die Oberfläche im Desktop-Client anders macht (37): Inhalte über <c>mydocs://</c> mit dem Gerätetoken,
/// Dateien mit der Standard-App, Abmelden durch Widerruf des Geräts. Ist der Server nicht erreichbar, zeigt der Client
/// „Offline“ statt der Anmeldung (38).
/// </summary>
public sealed partial class DesktopPlatform : IDesktopPlatform
{
    private readonly DeviceSession session;
    private readonly DesktopPaths paths;
    private readonly CheckoutEngine checkouts;
    private readonly IStringLocalizer<SharedResource> texts;

    public DesktopPlatform(DeviceSession session, DesktopPaths paths, CheckoutEngine checkouts, IStringLocalizer<SharedResource> texts)
    {
        this.session = session;
        this.paths = paths;
        this.checkouts = checkouts;
        this.texts = texts;
        checkouts.Changed += () => CheckoutsChanged?.Invoke();
        checkouts.Notice += notice => Notify?.Invoke(notice.Title, notice.Kind switch
        {
            CheckoutNoticeKind.Uploaded => texts["DesktopNoticeUploaded"],
            CheckoutNoticeKind.Conflict => texts["DesktopNoticeConflict"],
            _ => texts["DesktopNoticeOffline"],
        });
    }

    /// <summary>Benachrichtigung des Systems (vom Fenster gesetzt).</summary>
    public Action<string, string>? Notify { get; set; }

    public IReadOnlyList<CheckoutInfo> Checkouts =>
        [.. checkouts.Entries.OrderBy(e => e.CheckedOutAt).Select(e => new CheckoutInfo(e.DocumentId, e.Title, e.FileName, e.CheckedOutAt, e.Pending, e.Conflict, e.LastError))];

    public event Action? CheckoutsChanged;

    public async Task EditAsync(Guid documentId)
    {
        var entry = await checkouts.CheckOutAsync(documentId, CancellationToken.None);
        SystemShell.Open(entry.Path);
    }

    public async Task<bool> CheckInAsync(Guid documentId)
    {
        try
        {
            await checkouts.CheckInAsync(documentId, CancellationToken.None);
            return true;
        }
        catch (CheckInBlockedException)
        {
            return false;
        }
    }

    public Task DiscardAsync(Guid documentId) => checkouts.DiscardAsync(documentId, CancellationToken.None);

    /// <summary>Im Hintergrund: Vorgemerktes alle 30 Sekunden nachladen, Sperren alle 10 Minuten verlängern.</summary>
    public async Task RunBackgroundAsync(CancellationToken ct)
    {
        await checkouts.ResumeAsync(ct);
        var rounds = 0;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(ct))
        {
            if (session.State != ConnectionState.Connected)
            {
                continue;
            }

            await checkouts.SyncAllAsync(ct);
            if (++rounds % 20 == 0)
            {
                await checkouts.RenewAllAsync(ct);
            }
        }
    }
    public const string Scheme = "mydocs";

    /// <summary>Nur diese Pfade beantwortet das Schema; alles andere bleibt leer.</summary>
    [GeneratedRegex("^api/documents/[0-9a-fA-F-]{36}/(content|preview|thumbnail)(\\?version=[0-9]{1,6})?$")]
    private static partial Regex AllowedResource();

    /// <summary>Server antwortet nicht: Seite „Offline“ statt der Oberfläche.</summary>
    public bool Offline { get; private set; }

    public event Action? Changed;

    public string ResourceUrl(string apiPath) => $"{Scheme}://local/{apiPath}";

    public async Task OpenAsync(Guid documentId, int? version, string fileName)
    {
        using var api = session.CreateApiClient();
        using var response = await api.GetAsync(new Uri($"api/documents/{documentId}/content?download=true{(version is { } v ? $"&version={v}" : string.Empty)}", UriKind.Relative));
        response.EnsureSuccessStatusCode();
        DesktopPaths.EnsurePrivateDirectory(paths.Open);
        var folder = Path.Combine(paths.Open, Guid.NewGuid().ToString("N"));
        DesktopPaths.EnsurePrivateDirectory(folder);
        var path = Path.Combine(folder, Path.GetFileName(fileName));
        await using (var file = File.Create(path))
        {
            await response.Content.CopyToAsync(file);
        }

        // Schreibgeschützt: Änderungen gehören in einen Check-out (17), nicht in eine Kopie.
        File.SetAttributes(path, FileAttributes.ReadOnly);
        SystemShell.Open(path);
    }

    public Task DisconnectAsync() => session.DisconnectAsync(CancellationToken.None);

    public void ShowConnect()
    {
        if (session.State == ConnectionState.Connected)
        {
            Offline = true;
        }

        Changed?.Invoke();
    }

    public void Retry()
    {
        Offline = false;
        Changed?.Invoke();
    }

    /// <summary>Antwort für <c>mydocs://local/api/…</c> (Vorschau, Vorschaubild); läuft aus einem Rückruf der WebView, darum synchron.</summary>
    public Stream Resource(string url, out string contentType)
    {
        contentType = "text/plain";
        var path = url.StartsWith($"{Scheme}://local/", StringComparison.OrdinalIgnoreCase) ? url[$"{Scheme}://local/".Length..] : string.Empty;
        if (!AllowedResource().IsMatch(path) || session.State != ConnectionState.Connected)
        {
            return new MemoryStream();
        }

        // Eigener Thread ohne den Synchronisationskontext der Oberfläche, sonst blockiert sich der Aufruf selbst.
        var (type, bytes) = Task.Run(async () =>
        {
            using var api = session.CreateApiClient();
            using var response = await api.GetAsync(new Uri(path, UriKind.Relative));
            return response.IsSuccessStatusCode
                ? (response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream", await response.Content.ReadAsByteArrayAsync())
                : ("text/plain", Array.Empty<byte>());
        }).GetAwaiter().GetResult();
        contentType = type;
        return new MemoryStream(bytes);
    }

    /// <summary>Beim Start: Kopien schreibgeschützt geöffneter Dateien entfernen.</summary>
    public void CleanOpenFolder()
    {
        if (!Directory.Exists(paths.Open))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(paths.Open, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }
            catch (IOException)
            {
                // Noch geöffnet; beim nächsten Start.
            }
        }
    }
}
