using Microsoft.EntityFrameworkCore;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Audit;
using MyDocs.Server.Infrastructure.Files;
using MyDocs.Shared.Audit;

namespace MyDocs.Server.Infrastructure;

/// <summary>
/// Leert den Papierkorb (30): Objekte und Dokumente, die länger als 30 Tage gelöscht sind, verschwinden endgültig, mit
/// Versionen, Freigaben, Historie und Suchtext. Dokumente ohne Objekt verschwinden mit. Eine verschlüsselte Datei wird
/// erst entfernt, wenn keine Version mehr auf sie verweist (99). Der Eintrag im Audit-Log bleibt ohne Inhalt.
/// </summary>
public sealed partial class TrashPurge(IServiceProvider services, TimeProvider time, ILogger<TrashPurge> logger) : BackgroundService
{
    public static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), time, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception);
            }

            await Task.Delay(TimeSpan.FromHours(24), time, stoppingToken);
        }
    }

    /// <summary>Ein Durchlauf; liefert die Zahl endgültig gelöschter Objekte und Dokumente.</summary>
    public async Task<(int Objects, int Documents)> PurgeAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyDocsDbContext>();
        var blobs = scope.ServiceProvider.GetRequiredService<BlobStore>();
        var audit = scope.ServiceProvider.GetRequiredService<AuditLog>();
        var cutoff = time.GetUtcNow() - RetentionPeriod;

        var objects = await db.Objects.Where(o => o.IsDeleted && o.DeletedAt < cutoff).ToListAsync(ct);
        db.Objects.RemoveRange(objects);
        await db.SaveChangesAsync(ct);

        var documents = await db.Documents
            .Where(d => (d.IsDeleted && d.DeletedAt < cutoff) || !db.DocumentObjects.Any(l => l.DocumentId == d.Id))
            .ToListAsync(ct);
        var ids = documents.Select(d => d.Id).ToList();
        var versions = await db.DocumentVersions.Where(v => ids.Contains(v.DocumentId)).ToListAsync(ct);
        db.DocumentVersions.RemoveRange(versions);
        db.Documents.RemoveRange(documents);
        var hashes = versions.GroupBy(v => v.BlobHash).ToDictionary(g => g.Key, g => g.Count());
        var unused = await blobs.ReleaseAsync(hashes, ct);
        if (objects.Count + documents.Count > 0)
        {
            audit.Add(AuditActions.TrashPurged, actor: Guid.Empty, details: $"{objects.Count} Einträge, {documents.Count} Dokumente");
        }

        await db.SaveChangesAsync(ct);
        foreach (var hash in unused)
        {
            blobs.Delete(hash);
        }

        if (objects.Count + documents.Count > 0)
        {
            LogPurged(logger, objects.Count, documents.Count);
        }

        return (objects.Count, documents.Count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Papierkorb geleert: {Objects} Einträge, {Documents} Dokumente.")]
    private static partial void LogPurged(ILogger logger, int objects, int documents);

    [LoggerMessage(Level = LogLevel.Error, Message = "Leeren des Papierkorbs fehlgeschlagen.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
