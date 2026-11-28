using Microsoft.EntityFrameworkCore;
using MyDocs.Server.Data;
using MyDocs.Shared.Documents;

namespace MyDocs.Server.Infrastructure.Processing;

/// <summary>Weckt den Verarbeitungsdienst, sobald ein neuer Auftrag gespeichert ist.</summary>
public sealed class ProcessingSignal : IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Schon geweckt.
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);

    public void Dispose() => _signal.Dispose();
}

/// <summary>
/// Legt Aufträge für neue Inhalte an (24). Gespeichert wird mit dem nächsten <c>SaveChanges</c> des Aufrufers, danach
/// startet die Verarbeitung. Ein Inhalt, der schon einen Auftrag hat, bekommt keinen zweiten.
/// </summary>
public sealed class ProcessingScheduler
{
    private readonly MyDocsDbContext _db;
    private readonly TimeProvider _time;
    private bool _pending;

    public ProcessingScheduler(MyDocsDbContext db, TimeProvider time, ProcessingSignal signal)
    {
        _db = db;
        _time = time;
        db.SavedChanges += (_, _) =>
        {
            if (_pending)
            {
                _pending = false;
                signal.Notify();
            }
        };
    }

    public async Task EnqueueAsync(string hash, string fileName, CancellationToken ct)
    {
        if (_db.ProcessingJobs.Local.Any(j => j.SourceHash == hash) || await _db.ProcessingJobs.AnyAsync(j => j.SourceHash == hash, ct))
        {
            return;
        }

        var now = _time.GetUtcNow();
        _db.ProcessingJobs.Add(new ProcessingJob { SourceHash = hash, FileName = fileName, State = ProcessingStates.Pending, NextAttemptAt = now, CreatedAt = now });
        _pending = true;
    }

    /// <summary>Text eines schon verarbeiteten Inhalts für das Dokument, dessen aktuelle Version er wird; sonst null.</summary>
    public Task<string?> TextAsync(string hash, CancellationToken ct) =>
        _db.BlobTexts.Where(t => t.SourceHash == hash).Select(t => (string?)t.Text).FirstOrDefaultAsync(ct);

    /// <summary>Fehlgeschlagene Aufträge noch einmal von vorn (Verwaltung → Einstellungen).</summary>
    public async Task<int> RetryFailedAsync(CancellationToken ct)
    {
        var now = _time.GetUtcNow();
        var failed = await _db.ProcessingJobs.Where(j => j.State == ProcessingStates.Failed).ToListAsync(ct);
        foreach (var job in failed)
        {
            job.State = ProcessingStates.Pending;
            job.Attempts = 0;
            job.NextAttemptAt = now;
            job.Error = null;
        }

        _pending = failed.Count > 0;
        await _db.SaveChangesAsync(ct);
        return failed.Count;
    }
}
