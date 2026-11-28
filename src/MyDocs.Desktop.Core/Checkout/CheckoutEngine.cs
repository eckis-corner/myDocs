using System.Security.Cryptography;
using System.Text.Json;
using MyDocs.ApiClient;
using MyDocs.Shared.Documents;

namespace MyDocs.Desktop.Core.Checkout;

/// <summary>Eine ausgecheckte Datei; der Stand wird in <c>checkouts.json</c> gehalten und übersteht Neustarts.</summary>
public sealed record CheckoutEntry
{
    public Guid DocumentId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    /// <summary>Version, auf der die lokale Datei aufbaut.</summary>
    public int BaseVersion { get; init; }

    /// <summary>SHA-256 der zuletzt hochgeladenen bzw. heruntergeladenen Fassung.</summary>
    public string UploadedHash { get; init; } = string.Empty;

    public DateTimeOffset CheckedOutAt { get; init; }

    /// <summary>Geändert, aber noch nicht hochgeladen (etwa ohne Netz, 38).</summary>
    public bool Pending { get; init; }

    /// <summary>Es entstand ein Konfliktdokument; die Datei gehört jetzt zu ihm.</summary>
    public bool Conflict { get; init; }

    public string? LastError { get; init; }
}

/// <summary>Einchecken geht erst, wenn alle Änderungen oben sind.</summary>
public sealed class CheckInBlockedException(string message) : Exception(message);

/// <summary>Was der Nutzer erfahren soll (Benachrichtigung des Systems).</summary>
public sealed record CheckoutNotice(string Title, string FileName, CheckoutNoticeKind Kind);

public enum CheckoutNoticeKind
{
    Uploaded,
    Conflict,
    Offline,
}

/// <summary>
/// Check-out im Client (17, 156): herunterladen in einen eigenen Arbeitsordner, Änderungen über den File-Watcher
/// erkennen (entprellt, verglichen per SHA-256), jede gespeicherte Fassung als neue Version hochladen. Ohne Netz bleibt
/// sie vorgemerkt und geht nach, sobald der Server erreichbar ist. Bei einem Konflikt arbeitet die Datei am
/// Konfliktdokument weiter.
/// </summary>
public sealed class CheckoutEngine : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly DesktopPaths _paths;
    private readonly Func<HttpClient> _api;
    private readonly TimeProvider _time;
    private readonly TimeSpan _debounce;
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly Dictionary<Guid, FileSystemWatcher> _watchers = [];
    private readonly Dictionary<Guid, CancellationTokenSource> _pendingChanges = [];
    private List<CheckoutEntry> _entries = [];

    public CheckoutEngine(DesktopPaths paths, Func<HttpClient> api, TimeProvider time, TimeSpan? debounce = null)
    {
        _paths = paths;
        _api = api;
        _time = time;
        _debounce = debounce ?? TimeSpan.FromSeconds(2);
    }

    public IReadOnlyList<CheckoutEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return [.. _entries];
            }
        }
    }

    public event Action? Changed;

    public event Action<CheckoutNotice>? Notice;

    /// <summary>Beim Start: gespeicherte Check-outs übernehmen, beobachten und Vorgemerktes hochladen.</summary>
    public async Task ResumeAsync(CancellationToken ct)
    {
        if (File.Exists(_paths.Checkouts))
        {
            try
            {
                var stored = JsonSerializer.Deserialize<List<CheckoutEntry>>(await File.ReadAllTextAsync(_paths.Checkouts, ct), Json) ?? [];
                lock (_lock)
                {
                    _entries = [.. stored.Where(e => File.Exists(e.Path))];
                }
            }
            catch (JsonException)
            {
                // Beschädigte Datei: die Arbeitsordner bleiben, nur die Liste fehlt.
            }
        }

        foreach (var entry in Entries)
        {
            Watch(entry);
        }

        await SyncAllAsync(ct);
    }

    public async Task<CheckoutEntry> CheckOutAsync(Guid documentId, CancellationToken ct)
    {
        if (Find(documentId) is { } existing)
        {
            return existing;
        }

        using var http = _api();
        var documents = new DocumentClient(http);
        var checkout = await documents.CheckOutAsync(documentId, ct);
        var version = checkout.Document.Versions.First(v => v.Number == checkout.BaseVersion);
        var folder = System.IO.Path.Combine(_paths.Work, documentId.ToString("N"));
        DesktopPaths.EnsurePrivateDirectory(_paths.Work);
        DesktopPaths.EnsurePrivateDirectory(folder);
        var path = System.IO.Path.Combine(folder, System.IO.Path.GetFileName(version.FileName));
        await documents.DownloadAsync(documentId, checkout.BaseVersion, path, ct);
        var entry = new CheckoutEntry
        {
            DocumentId = documentId,
            Title = checkout.Document.Title,
            FileName = version.FileName,
            Path = path,
            BaseVersion = checkout.BaseVersion,
            UploadedHash = await HashAsync(path, ct),
            CheckedOutAt = _time.GetUtcNow(),
        };
        Replace(null, entry);
        Watch(entry);
        return entry;
    }

    /// <summary>Lädt eine geänderte Datei hoch; ohne Netz bleibt sie vorgemerkt.</summary>
    public async Task SyncAsync(Guid documentId, CancellationToken ct)
    {
        await _sync.WaitAsync(ct);
        try
        {
            if (Find(documentId) is not { } entry || !File.Exists(entry.Path))
            {
                return;
            }

            string hash;
            try
            {
                hash = await HashAsync(entry.Path, ct);
            }
            catch (IOException)
            {
                // Die Office-App schreibt noch: später.
                Replace(entry, entry with { Pending = true });
                return;
            }

            if (hash == entry.UploadedHash)
            {
                if (entry.Pending)
                {
                    Replace(entry, entry with { Pending = false, LastError = null });
                }

                return;
            }

            try
            {
                using var http = _api();
                CheckoutUploadResult result;
                await using (var file = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    result = await new DocumentClient(http).UploadCheckoutAsync(entry.DocumentId, entry.BaseVersion, file, entry.FileName, "application/octet-stream", ct);
                }

                var next = entry with
                {
                    DocumentId = result.Document.Id,
                    Title = result.Document.Title,
                    BaseVersion = result.BaseVersion,
                    UploadedHash = hash,
                    Pending = false,
                    Conflict = entry.Conflict || result.Conflict,
                    LastError = null,
                };
                if (result.Conflict)
                {
                    Unwatch(entry.DocumentId);
                    Replace(entry, next);
                    Watch(next);
                    Notice?.Invoke(new CheckoutNotice(next.Title, next.FileName, CheckoutNoticeKind.Conflict));
                }
                else
                {
                    Replace(entry, next);
                    Notice?.Invoke(new CheckoutNotice(next.Title, next.FileName, CheckoutNoticeKind.Uploaded));
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException)
            {
                if (!entry.Pending)
                {
                    Notice?.Invoke(new CheckoutNotice(entry.Title, entry.FileName, CheckoutNoticeKind.Offline));
                }

                Replace(entry, entry with { Pending = true, LastError = exception.Message });
            }
            catch (ApiException exception)
            {
                Replace(entry, entry with { Pending = true, LastError = exception.Code });
            }
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task SyncAllAsync(CancellationToken ct)
    {
        foreach (var entry in Entries)
        {
            await SyncAsync(entry.DocumentId, ct);
        }
    }

    /// <summary>Hält die Sperren am Leben, solange der Client läuft (alle 10 Minuten).</summary>
    public async Task RenewAllAsync(CancellationToken ct)
    {
        foreach (var entry in Entries)
        {
            try
            {
                using var http = _api();
                await new DocumentClient(http).RenewCheckoutAsync(entry.DocumentId, ct);
            }
            catch (Exception exception) when (exception is HttpRequestException or ApiException or TaskCanceledException)
            {
                // Ohne Netz oder mit verlorener Sperre: Der nächste Upload klärt es (notfalls als Konflikt).
            }
        }
    }

    /// <summary>Letzte Änderungen hochladen, Sperre aufheben, lokale Kopie entfernen.</summary>
    public async Task CheckInAsync(Guid documentId, CancellationToken ct)
    {
        await SyncAsync(documentId, ct);
        if (Find(documentId) is not { } entry)
        {
            return;
        }

        if (entry.Pending)
        {
            throw new CheckInBlockedException(entry.LastError ?? "Änderungen sind noch nicht hochgeladen.");
        }

        using (var http = _api())
        {
            await new DocumentClient(http).CheckInAsync(documentId, ct);
        }

        Remove(entry);
    }

    /// <summary>Lokale Kopie verwerfen, ohne hochzuladen; die Sperre wird aufgehoben, wenn der Server erreichbar ist.</summary>
    public async Task DiscardAsync(Guid documentId, CancellationToken ct)
    {
        if (Find(documentId) is not { } entry)
        {
            return;
        }

        try
        {
            using var http = _api();
            await new DocumentClient(http).CheckInAsync(documentId, ct);
        }
        catch (Exception exception) when (exception is HttpRequestException or ApiException)
        {
            // Die Sperre läuft nach 24 Stunden ab.
        }

        Remove(entry);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var watcher in _watchers.Values)
            {
                watcher.Dispose();
            }

            foreach (var pending in _pendingChanges.Values)
            {
                pending.Cancel();
                pending.Dispose();
            }

            _watchers.Clear();
            _pendingChanges.Clear();
        }

        _sync.Dispose();
    }

    private CheckoutEntry? Find(Guid documentId) => Entries.FirstOrDefault(e => e.DocumentId == documentId);

    private void Remove(CheckoutEntry entry)
    {
        Unwatch(entry.DocumentId);
        lock (_lock)
        {
            _entries.RemoveAll(e => e.Path == entry.Path);
            Save();
        }

        try
        {
            Directory.Delete(System.IO.Path.GetDirectoryName(entry.Path)!, recursive: true);
        }
        catch (IOException)
        {
            // Noch in der Office-App geöffnet; der Ordner bleibt, die Datei ist eingecheckt.
        }

        Changed?.Invoke();
    }

    private void Replace(CheckoutEntry? old, CheckoutEntry next)
    {
        lock (_lock)
        {
            if (old is not null)
            {
                _entries.RemoveAll(e => e.Path == old.Path);
            }

            _entries.Add(next);
            Save();
        }

        Changed?.Invoke();
    }

    private void Save()
    {
        DesktopPaths.EnsurePrivateDirectory(_paths.Root);
        var temporary = _paths.Checkouts + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_entries, Json));
        File.Move(temporary, _paths.Checkouts, overwrite: true);
    }

    private void Watch(CheckoutEntry entry)
    {
        var watcher = new FileSystemWatcher(System.IO.Path.GetDirectoryName(entry.Path)!)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            IncludeSubdirectories = false,
        };
        var name = System.IO.Path.GetFileName(entry.Path);
        void OnChange(object? sender, FileSystemEventArgs e)
        {
            // Office speichert oft über eine Hilfsdatei und Umbenennen; nur die Datei selbst zählt.
            if (string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase) || (e is RenamedEventArgs r && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                Schedule(entry.DocumentId);
            }
        }

        watcher.Changed += OnChange;
        watcher.Created += OnChange;
        watcher.Renamed += (sender, e) => OnChange(sender, e);
        watcher.EnableRaisingEvents = true;
        lock (_lock)
        {
            if (_watchers.Remove(entry.DocumentId, out var old))
            {
                old.Dispose();
            }

            _watchers[entry.DocumentId] = watcher;
        }
    }

    private void Unwatch(Guid documentId)
    {
        lock (_lock)
        {
            if (_watchers.Remove(documentId, out var watcher))
            {
                watcher.Dispose();
            }
        }
    }

    /// <summary>Entprellt: Erst wenn eine Weile nichts mehr geschrieben wurde, wird hochgeladen.</summary>
    private void Schedule(Guid documentId)
    {
        CancellationTokenSource next;
        lock (_lock)
        {
            if (_pendingChanges.Remove(documentId, out var previous))
            {
                previous.Cancel();
                previous.Dispose();
            }

            next = new CancellationTokenSource();
            _pendingChanges[documentId] = next;
        }

        var token = next.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_debounce, token);
                // Eine neue Datei-ID nach einem Konflikt: über den Pfad zurückfinden.
                var current = Entries.FirstOrDefault(e => e.DocumentId == documentId)?.DocumentId ?? documentId;
                await SyncAsync(current, CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }, CancellationToken.None);
    }

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct));
    }
}
