using System.Net;
using MyDocs.ApiClient;
using MyDocs.Shared.Documents;

namespace MyDocs.Desktop.Core.Inbox;

/// <summary>
/// Eingangsordner am Desktop (19, 159): Was in den Ordner gelegt wird, geht in den eigenen Posteingang und danach in
/// den Unterordner „hochgeladen“. Übernommen wird eine Datei erst, wenn Größe und Änderungszeit bei zwei Durchläufen
/// gleich sind. Ohne Netz bleibt sie liegen; was der Server ablehnt, kommt nach „nicht hochgeladen“.
/// </summary>
public sealed class InboxFolderWatcher(Func<HttpClient> api)
{
    public const string UploadedFolder = "hochgeladen";
    public const string RejectedFolder = "nicht hochgeladen";

    private readonly Dictionary<string, (long Size, DateTime Modified)> _seen = new(StringComparer.Ordinal);

    /// <summary>Eine Datei ist im Posteingang angekommen (für die Benachrichtigung).</summary>
    public event Action<string>? Uploaded;

    public async Task<int> ScanOnceAsync(string? folder, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return 0;
        }

        var present = new HashSet<string>(StringComparer.Ordinal);
        var uploaded = 0;
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(file);
            if (Ignored(name))
            {
                continue;
            }

            present.Add(file);
            var info = new FileInfo(file);
            var current = (info.Length, info.LastWriteTimeUtc);
            if (!_seen.TryGetValue(file, out var previous) || previous != current || current.Length == 0)
            {
                _seen[file] = current;
                continue;
            }

            try
            {
                using var http = api();
                await using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    await new InboxClient(http).UploadAsync(stream, name, "application/octet-stream", household: false, DocumentSources.Desktop, ct);
                }

                Move(folder, file, UploadedFolder);
                _seen.Remove(file);
                uploaded++;
                Uploaded?.Invoke(name);
            }
            catch (ApiException exception) when (exception.Status is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge)
            {
                Move(folder, file, RejectedFolder);
                _seen.Remove(file);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException or ApiException)
            {
                // Ohne Netz oder noch gesperrt: beim nächsten Durchlauf.
            }
        }

        foreach (var gone in _seen.Keys.Where(k => !present.Contains(k)).ToList())
        {
            _seen.Remove(gone);
        }

        return uploaded;
    }

    /// <summary>Versteckte Dateien und unfertige Downloads oder Office-Sperrdateien bleiben liegen.</summary>
    internal static bool Ignored(string name) =>
        name.StartsWith('.') || name.StartsWith("~$", StringComparison.Ordinal)
        || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".crdownload", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".part", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".download", StringComparison.OrdinalIgnoreCase)
        || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) || name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase);

    private static void Move(string folder, string file, string target)
    {
        var directory = Path.Combine(folder, target);
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, Path.GetFileName(file));
        if (File.Exists(destination))
        {
            destination = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(file)} ({DateTime.Now:yyyy-MM-dd HH-mm-ss}){Path.GetExtension(file)}");
        }

        File.Move(file, destination);
    }
}
