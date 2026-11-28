using System.Text;
using Microsoft.EntityFrameworkCore;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Files;
using MyDocs.Shared.Documents;

namespace MyDocs.Server.Infrastructure.Processing;

/// <summary>
/// Ein Auftrag (24): Art erkennen → PDF (Office über LibreOffice, Bilder und Scans über OCR, PDF mit Text bleibt) →
/// Text → Vorschaubild. Lehnt der Worker einen Schritt ab, geht es mit dem Rest weiter und der Fehler steht am Auftrag;
/// ist er nicht erreichbar, kommt der Auftrag später wieder. Klartext liegt dabei nie auf der Platte der App: Er geht
/// verschlüsselt gelesen in die Anfrage und kommt verschlüsselt geschrieben aus der Antwort.
/// </summary>
public sealed partial class DocumentProcessor(MyDocsDbContext db, BlobStore blobs, IDocumentWorker worker, TimeProvider time, ILogger<DocumentProcessor> logger)
{
    /// <summary>Höchstens so viel erkannter Text je Inhalt (etwa 100 Seiten); reicht für die Suche und bleibt unter der Grenze von tsvector.</summary>
    public const int MaxTextLength = 300_000;

    /// <summary>Unter so vielen Buchstaben je Seite gilt ein PDF als Scan ohne Text.</summary>
    internal const int MinLettersPerPage = 30;

    private static readonly int[] BackoffMinutes = [1, 10, 60, 360, 1440];

    public static int MaxAttempts => BackoffMinutes.Length + 1;

    /// <summary>Bearbeitet den ältesten fälligen Auftrag; false, wenn keiner wartet.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var job = await db.ProcessingJobs.Where(j => j.State == ProcessingStates.Pending && j.NextAttemptAt <= now)
            .OrderBy(j => j.CreatedAt).FirstOrDefaultAsync(ct);
        if (job is null)
        {
            return false;
        }

        try
        {
            await ProcessAsync(job, ct);
        }
        catch (WorkerUnavailableException exception)
        {
            job.Attempts++;
            job.Error = Shorten(exception.Message);
            if (job.Attempts >= MaxAttempts)
            {
                job.State = ProcessingStates.Failed;
                job.CompletedAt = time.GetUtcNow();
                LogGaveUp(logger, job.SourceHash[..12], job.Attempts);
            }
            else
            {
                job.NextAttemptAt = time.GetUtcNow().AddMinutes(BackoffMinutes[job.Attempts - 1]);
            }
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task ProcessAsync(ProcessingJob job, CancellationToken ct)
    {
        // Ein abgebrochener Durchlauf (Neustart, Worker weg) kann Ableitungen hinterlassen haben: von vorn beginnen.
        await ResetAsync(job.SourceHash, ct);

        var source = await db.Blobs.AsNoTracking().FirstAsync(b => b.Hash == job.SourceHash, ct);
        var header = await blobs.ReadPrefixAsync(job.SourceHash, FileKinds.HeaderLength, ct);
        var kind = FileKinds.Detect(header, job.FileName);
        job.Kind = kind;
        var errors = new List<string>();
        var original = Input(source.Hash, source.Size);
        string? pdf = null;
        var text = string.Empty;

        switch (kind)
        {
            case FileKind.Pdf:
                text = (await StepAsync(() => worker.TextAsync(original, ct), "text", errors)).Value ?? string.Empty;
                if (!HasText(text))
                {
                    pdf = await StorePdfAsync(job.SourceHash, original, "pdf", ".pdf", errors, ct);
                    if (pdf is not null)
                    {
                        text = await TextOfAsync(pdf, errors, ct);
                    }
                }

                await StoreThumbnailAsync(job.SourceHash, pdf ?? job.SourceHash, errors, ct);
                break;
            case FileKind.Image or FileKind.Office or FileKind.Text:
                pdf = await StorePdfAsync(job.SourceHash, original, kind == FileKind.Image ? "image" : "office", FileKinds.ExtensionFor(kind, job.FileName, header), errors, ct);
                if (pdf is not null)
                {
                    text = await TextOfAsync(pdf, errors, ct);
                    await StoreThumbnailAsync(job.SourceHash, pdf, errors, ct);
                }

                break;
        }

        var clean = Normalize(text);
        if (clean.Length > 0)
        {
            db.BlobTexts.Add(new BlobText { SourceHash = job.SourceHash, Text = clean });
        }

        // Dokumente, deren aktuelle Version diesen Inhalt hat, werden über den Text auffindbar (M3.2).
        var documentIds = await db.DocumentVersions
            .Where(v => v.BlobHash == job.SourceHash && v.Number == db.DocumentVersions.Where(w => w.DocumentId == v.DocumentId).Max(w => w.Number))
            .Select(v => v.DocumentId).ToListAsync(ct);
        foreach (var document in await db.Documents.Where(d => documentIds.Contains(d.Id)).ToListAsync(ct))
        {
            document.ContentText = clean.Length > 0 ? clean : null;
        }

        job.State = ProcessingStates.Done;
        job.Error = errors.Count > 0 ? Shorten(string.Join("; ", errors)) : null;
        job.CompletedAt = time.GetUtcNow();
        LogDone(logger, job.SourceHash[..12], kind, pdf is not null, clean.Length, errors.Count);
    }

    /// <summary>Verwirft Ableitungen und Text eines Inhalts, etwa vor einem neuen Durchlauf.</summary>
    private async Task ResetAsync(string hash, CancellationToken ct)
    {
        var derivatives = await db.BlobDerivatives.Where(d => d.SourceHash == hash).ToListAsync(ct);
        if (derivatives.Count == 0 && !await db.BlobTexts.AnyAsync(t => t.SourceHash == hash, ct))
        {
            return;
        }

        db.BlobDerivatives.RemoveRange(derivatives);
        db.BlobTexts.RemoveRange(await db.BlobTexts.Where(t => t.SourceHash == hash).ToListAsync(ct));
        var unused = await blobs.ReleaseAsync(derivatives.GroupBy(d => d.BlobHash).ToDictionary(g => g.Key, g => g.Count()), ct);
        await db.SaveChangesAsync(ct);
        foreach (var file in unused)
        {
            blobs.Delete(file);
        }
    }

    private async Task<string?> StorePdfAsync(string sourceHash, WorkerInput input, string source, string extension, List<string> errors, CancellationToken ct)
    {
        var stored = await StepAsync(() => worker.PdfAsync(input, source, extension, stream => blobs.StoreAsync(stream, long.MaxValue, ct), ct), "pdf", errors);
        if (!stored.Ok)
        {
            return null;
        }

        await AddDerivativeAsync(sourceHash, DerivativeKinds.Pdf, stored.Value.Hash, "application/pdf", stored.Value.Size, ct);
        return stored.Value.Hash;
    }

    private async Task StoreThumbnailAsync(string sourceHash, string pdfHash, List<string> errors, CancellationToken ct)
    {
        var size = await db.Blobs.Where(b => b.Hash == pdfHash).Select(b => b.Size).FirstAsync(ct);
        var stored = await StepAsync(() => worker.ThumbnailAsync(Input(pdfHash, size), stream => blobs.StoreAsync(stream, long.MaxValue, ct), ct), "thumbnail", errors);
        if (stored.Ok)
        {
            await AddDerivativeAsync(sourceHash, DerivativeKinds.Thumbnail, stored.Value.Hash, "image/png", stored.Value.Size, ct);
        }
    }

    private async Task<string> TextOfAsync(string pdfHash, List<string> errors, CancellationToken ct)
    {
        var size = await db.Blobs.Where(b => b.Hash == pdfHash).Select(b => b.Size).FirstAsync(ct);
        return (await StepAsync(() => worker.TextAsync(Input(pdfHash, size), ct), "text", errors)).Value ?? string.Empty;
    }

    /// <summary>Gleich speichern: Bricht der Durchlauf danach ab, findet <see cref="ResetAsync"/> den Blob wieder.</summary>
    private async Task AddDerivativeAsync(string sourceHash, string kind, string hash, string contentType, long size, CancellationToken ct)
    {
        db.BlobDerivatives.Add(new BlobDerivative { SourceHash = sourceHash, Kind = kind, BlobHash = hash, ContentType = contentType, Size = size, CreatedAt = time.GetUtcNow() });
        await db.SaveChangesAsync(ct);
    }

    private static async Task<(bool Ok, T? Value)> StepAsync<T>(Func<Task<T>> step, string name, List<string> errors)
    {
        try
        {
            return (true, await step());
        }
        catch (WorkerRejectedException exception)
        {
            errors.Add($"{name}: {exception.Message}");
            return (false, default);
        }
    }

    private WorkerInput Input(string hash, long size) => new((output, ct) => blobs.CopyToAsync(hash, output, ct), size);

    /// <summary>Hat das PDF eine brauchbare Textebene? pdftotext trennt Seiten mit \f.</summary>
    internal static bool HasText(string text)
    {
        var pages = Math.Max(1, text.Count(c => c == '\f'));
        return text.Count(char.IsLetter) >= pages * MinLettersPerPage;
    }

    /// <summary>Seitenvorschübe zu Leerzeilen, keine Nullbytes (PostgreSQL), höchstens eine Leerzeile am Stück, gekürzt.</summary>
    internal static string Normalize(string text)
    {
        var result = new StringBuilder(Math.Min(text.Length, MaxTextLength));
        var blank = 0;
        foreach (var raw in text.Replace('\f', '\n').Replace("\0", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.TrimEnd();
            blank = line.Length == 0 ? blank + 1 : 0;
            if (blank > 1 || (blank == 1 && result.Length == 0))
            {
                continue;
            }

            if (result.Length + line.Length + 1 > MaxTextLength)
            {
                break;
            }

            result.Append(line).Append('\n');
        }

        return result.ToString().Trim();
    }

    private static string Shorten(string value) => value.Length > 500 ? value[..500] : value;

    [LoggerMessage(Level = LogLevel.Information, Message = "Verarbeitet {Hash}: {Kind}, PDF {Pdf}, {Length} Zeichen Text, {Errors} Fehler.")]
    private static partial void LogDone(ILogger logger, string hash, string kind, bool pdf, int length, int errors);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Verarbeitung von {Hash} nach {Attempts} Versuchen aufgegeben.")]
    private static partial void LogGaveUp(ILogger logger, string hash, int attempts);
}

/// <summary>Arbeitet die Aufträge nacheinander ab, sobald einer kommt, sonst jede Minute (24).</summary>
public sealed partial class ProcessingService(IServiceScopeFactory scopes, IDocumentWorker worker, ProcessingSignal signal, ILogger<ProcessingService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public override void Dispose()
    {
        _gate.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!worker.IsConfigured)
        {
            LogNotConfigured(logger);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (worker.IsConfigured)
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException or Npgsql.NpgsqlException or IOException)
                {
                    LogFailed(logger, exception);
                }
            }

            try
            {
                await signal.WaitAsync(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Alle fälligen Aufträge; liefert ihre Zahl (für Tests).</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var count = 0;
            while (true)
            {
                await using var scope = scopes.CreateAsyncScope();
                if (!await scope.ServiceProvider.GetRequiredService<DocumentProcessor>().ProcessNextAsync(ct))
                {
                    return count;
                }

                count++;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kein Worker eingerichtet (MyDocs__WorkerUrl): Dokumente werden gespeichert, aber nicht verarbeitet.")]
    private static partial void LogNotConfigured(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Verarbeitung fehlgeschlagen.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
