using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Audit;
using MyDocs.Server.Infrastructure.Files;
using MyDocs.Server.Infrastructure.Processing;
using MyDocs.Shared.Documents;

namespace MyDocs.Server.Infrastructure.Inbox;

/// <summary>
/// Scan-Ordner auf der Synology (19, 147): Dateien direkt im Ordner gehen in den Posteingang des Haushalts, Dateien in
/// einem Unterordner, der wie die E-Mail-Adresse eines Kontos heißt, in dessen privaten Posteingang. Übernommen wird
/// eine Datei erst, wenn Größe und Änderungszeit bei zwei Durchläufen gleich sind (der Scanner ist fertig); danach wird
/// sie gelöscht, damit kein Klartext liegen bleibt. Was nicht passt, kommt in den Unterordner <c>_fehler</c>.
/// </summary>
public sealed partial class ScanFolderService(IServiceScopeFactory scopes, IOptions<MyDocsOptions> options, TimeProvider time,
    ILogger<ScanFolderService> logger) : BackgroundService
{
    public const string ErrorFolder = "_fehler";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private static readonly HashSet<string> IgnoredFolders = new(StringComparer.OrdinalIgnoreCase) { ErrorFolder, "@eaDir", "#recycle", "#snapshot" };
    private readonly ConcurrentDictionary<string, (long Size, DateTime Modified)> _seen = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unknownFolders = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<(string, long, DateTime)> _stuck = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string? Directory => string.IsNullOrWhiteSpace(options.Value.ScanDirectory) ? null : Path.GetFullPath(options.Value.ScanDirectory);

    public bool IsActive => Directory is { } directory && System.IO.Directory.Exists(directory);

    public override void Dispose()
    {
        _gate.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (Directory is null)
        {
            return;
        }

        LogWatching(logger, Directory);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DbUpdateException or Npgsql.NpgsqlException)
            {
                LogFailed(logger, exception);
            }

            try
            {
                await Task.Delay(PollInterval, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Ein Durchlauf; liefert die Zahl übernommener Dateien.</summary>
    public async Task<int> ScanOnceAsync(CancellationToken ct)
    {
        if (Directory is not { } root || !System.IO.Directory.Exists(root))
        {
            return 0;
        }

        await _gate.WaitAsync(ct);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MyDocsDbContext>();
            var people = await (from u in db.Users
                                join p in db.Persons on u.Id equals p.UserId
                                where u.Email != null
                                select new { u.Email, PersonId = p.Id }).ToDictionaryAsync(x => x.Email!, x => x.PersonId, StringComparer.OrdinalIgnoreCase, ct);

            var targets = new List<(string Folder, Guid? Person)> { (root, null) };
            foreach (var folder in System.IO.Directory.EnumerateDirectories(root))
            {
                var name = Path.GetFileName(folder);
                if (IgnoredFolders.Contains(name) || name.StartsWith('.'))
                {
                    continue;
                }

                if (people.TryGetValue(name, out var person))
                {
                    targets.Add((folder, person));
                }
                else if (_unknownFolders.Add(name))
                {
                    LogUnknownFolder(logger, name);
                }
            }

            var present = new HashSet<string>(StringComparer.Ordinal);
            var imported = 0;
            foreach (var (folder, person) in targets)
            {
                foreach (var file in System.IO.Directory.EnumerateFiles(folder))
                {
                    var name = Path.GetFileName(file);
                    if (name.StartsWith('.') || name.StartsWith('~'))
                    {
                        continue;
                    }

                    present.Add(file);
                    var info = new FileInfo(file);
                    var current = (info.Length, info.LastWriteTimeUtc);
                    // Erst übernehmen, wenn sich seit dem letzten Durchlauf nichts mehr geändert hat.
                    if (!_seen.TryGetValue(file, out var previous) || previous != current || current.Length == 0)
                    {
                        _seen[file] = current;
                        continue;
                    }

                    if (_stuck.Contains((file, current.Length, current.LastWriteTimeUtc)))
                    {
                        // Schon übernommen, aber nicht löschbar: kein zweites Mal.
                        continue;
                    }

                    if (await ImportAsync(scope.ServiceProvider, root, file, person, ct))
                    {
                        imported++;
                        if (File.Exists(file))
                        {
                            _stuck.Add((file, current.Length, current.LastWriteTimeUtc));
                        }
                    }

                    _seen.TryRemove(file, out _);
                }
            }

            foreach (var gone in _seen.Keys.Where(k => !present.Contains(k)).ToList())
            {
                _seen.TryRemove(gone, out _);
            }

            return imported;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> ImportAsync(IServiceProvider services, string root, string file, Guid? person, CancellationToken ct)
    {
        var name = Path.GetFileName(file);
        var maxBytes = options.Value.MaxFileSizeMegabytes * 1024L * 1024L;
        var header = new byte[FileKinds.HeaderLength];
        int read;
        await using (var probe = File.OpenRead(file))
        {
            read = await probe.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        }

        var kind = FileKinds.Detect(header.AsSpan(0, read), name);
        if (kind is not (FileKind.Pdf or FileKind.Image))
        {
            MoveToErrors(root, file, "keine PDF- oder Bilddatei");
            return false;
        }

        if (new FileInfo(file).Length > maxBytes)
        {
            MoveToErrors(root, file, "zu groß");
            return false;
        }

        var db = services.GetRequiredService<MyDocsDbContext>();
        var blobs = services.GetRequiredService<BlobStore>();
        var processing = services.GetRequiredService<ProcessingScheduler>();
        var audit = services.GetRequiredService<AuditLog>();
        (string Hash, long Size) blob;
        await using (var input = File.OpenRead(file))
        {
            blob = await blobs.StoreAsync(input, maxBytes, ct);
        }

        var contentType = kind == FileKind.Pdf ? "application/pdf"
            : header[0] == 0xFF ? "image/jpeg" : header[0] == 0x89 ? "image/png" : "image/tiff";
        var relative = Path.GetRelativePath(root, file);
        await InboxIntake.AddAsync(db, processing, audit, blob.Hash, blob.Size, name, contentType, person, DocumentSources.Scan, relative, Guid.Empty, time.GetUtcNow(), ct);
        await db.SaveChangesAsync(ct);
        try
        {
            File.Delete(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Bleibt sie liegen, käme sie beim nächsten Durchlauf wieder: Rechte des Ordners prüfen (installation.md).
            LogNotDeleted(logger, relative, exception);
        }

        LogImported(logger, relative, person is null ? "Haushalt" : "privat");
        return true;
    }

    private void MoveToErrors(string root, string file, string reason)
    {
        var target = Path.Combine(root, ErrorFolder);
        System.IO.Directory.CreateDirectory(target);
        var destination = Path.Combine(target, Path.GetFileName(file));
        if (File.Exists(destination))
        {
            destination = Path.Combine(target, $"{Path.GetFileNameWithoutExtension(file)}-{Guid.NewGuid():N}{Path.GetExtension(file)}");
        }

        File.Move(file, destination);
        LogRejected(logger, Path.GetRelativePath(root, file), reason);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Scan-Ordner {Directory} wird beobachtet.")]
    private static partial void LogWatching(ILogger logger, string directory);

    [LoggerMessage(Level = LogLevel.Information, Message = "Scan übernommen: {File} ({Inbox}).")]
    private static partial void LogImported(ILogger logger, string file, string inbox);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Scan nach _fehler verschoben: {File} ({Reason}).")]
    private static partial void LogRejected(ILogger logger, string file, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unterordner {Folder} im Scan-Ordner gehört zu keinem Konto und wird übergangen.")]
    private static partial void LogUnknownFolder(ILogger logger, string folder);

    [LoggerMessage(Level = LogLevel.Error, Message = "Scan {File} übernommen, aber nicht gelöscht.")]
    private static partial void LogNotDeleted(ILogger logger, string file, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Scan-Ordner: Durchlauf fehlgeschlagen.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
