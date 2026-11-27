using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Audit;
using MyDocs.Shared.Admin;
using MyDocs.Shared.Audit;

namespace MyDocs.Server.Infrastructure.Backup;

/// <summary>
/// Tägliche Sicherung (45, wie myLife): zur eingestellten Uhrzeit eine verschlüsselte Sicherung in
/// <c>{DataDirectory}/backup</c>, danach Aufräumen nach Generationen. Das Synology-Backup übernimmt den Ordner.
/// </summary>
public sealed partial class BackupService(IServiceScopeFactory scopes, IOptions<MyDocsOptions> options, TimeProvider time, ILogger<BackupService> logger)
    : BackgroundService
{
    private const string Prefix = "mydocs-";
    private const string NameFormat = "yyyyMMdd-HHmmss";
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly byte[]? _key = BackupFormat.ParseKey(options.Value.BackupKey);

    public string Directory { get; } = Path.Combine(Path.GetFullPath(options.Value.DataDirectory), "backup");

    public bool Configured => _key is not null;

    public string? LastError { get; private set; }

    public DateTimeOffset? LastErrorAt { get; private set; }

    public override void Dispose()
    {
        _gate.Dispose();
        base.Dispose();
    }

    public IReadOnlyList<BackupItemDto> List() =>
        !System.IO.Directory.Exists(Directory)
            ? []
            : [.. new DirectoryInfo(Directory).GetFiles($"{Prefix}*{BackupFormat.FileExtension}")
                .Select(f => new BackupItemDto(f.Name, CreatedAt(f), f.Length))
                .OrderByDescending(i => i.CreatedAt)];

    public BackupStatusDto Status()
    {
        var items = List();
        return new BackupStatusDto(Configured, items.Count > 0 ? items[0].CreatedAt : null, LastError, LastErrorAt, Configured ? NextRun(items) : null, items);
    }

    /// <summary>Jetzt sichern (Knopf in der Verwaltung oder Zeitplan); danach alte Generationen aufräumen.</summary>
    public async Task<BackupItemDto> RunAsync(Guid? actorId, CancellationToken ct)
    {
        if (_key is null)
        {
            throw new InvalidOperationException("Kein Sicherungsschlüssel eingerichtet (MYDOCS_BACKUP_KEY).");
        }

        await _gate.WaitAsync(ct);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyDocsDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<AuditLog>();
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var now = time.GetUtcNow();
            var stamp = now.UtcDateTime.ToString(NameFormat, CultureInfo.InvariantCulture);
            var path = Path.Combine(Directory, $"{Prefix}{stamp}{BackupFormat.FileExtension}");
            for (var n = 2; File.Exists(path); n++)
            {
                path = Path.Combine(Directory, $"{Prefix}{stamp}-{n.ToString(CultureInfo.InvariantCulture)}{BackupFormat.FileExtension}");
            }

            var result = await BackupArchive.CreateAsync(db, Path.GetFullPath(options.Value.DataDirectory), _key, path, options.Value.BackupIncludeDocuments, now, ct);
            var created = Path.GetFileName(result.Path);
            foreach (var old in ToDelete([.. List().Select(i => (i.Name, i.CreatedAt))], options.Value, TimeZoneInfo.Local).Where(n => n != created))
            {
                File.Delete(Path.Combine(Directory, old));
            }

            LastError = null;
            LastErrorAt = null;
            await audit.WriteAsync(AuditActions.BackupCreated, actorId ?? Guid.Empty,
                details: string.Create(CultureInfo.InvariantCulture, $"{created}, {result.Size} Byte, {result.Documents} Dateien"), ct: ct);
            LogCreated(logger, created, result.Size, result.Documents);
            return new BackupItemDto(created, now, result.Size);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Npgsql.NpgsqlException or DbUpdateException or InvalidOperationException)
        {
            LastError = exception.Message;
            LastErrorAt = time.GetUtcNow();
            LogFailed(logger, exception);
            db.ChangeTracker.Clear();
            await audit.WriteAsync(AuditActions.BackupFailed, actorId ?? Guid.Empty, details: exception.GetType().Name, ct: ct);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Ein Durchlauf des Zeitplans: sichern, wenn heute (Ortszeit) noch keine Sicherung da ist und die Uhrzeit erreicht ist.</summary>
    public async Task<bool> ProcessOnceAsync(CancellationToken ct)
    {
        if (!Configured || NextRun(List()) is not { } due || time.GetUtcNow() < due)
        {
            return false;
        }

        try
        {
            await RunAsync(null, ct);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Npgsql.NpgsqlException or DbUpdateException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Nächster Lauf: heute zur Uhrzeit, wenn heute noch keine Sicherung da ist, sonst morgen.</summary>
    public DateTimeOffset? NextRun(IReadOnlyList<BackupItemDto> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var zone = TimeZoneInfo.Local;
        var today = TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).Date;
        var doneToday = items.Any(i => TimeZoneInfo.ConvertTime(i.CreatedAt, zone).Date == today);
        var local = (doneToday ? today.AddDays(1) : today).AddHours(Math.Clamp(options.Value.BackupHour, 0, 23));
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    /// <summary>Generationen: die neueste je Tag, je ISO-Woche und je Monat für die eingestellte Anzahl; die neueste bleibt immer.</summary>
    public static IReadOnlyList<string> ToDelete(IReadOnlyList<(string Name, DateTimeOffset CreatedAt)> items, MyDocsOptions keep, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(keep);
        var ordered = items.OrderByDescending(i => i.CreatedAt).ToList();
        var kept = new HashSet<string>(StringComparer.Ordinal);
        if (ordered.Count > 0)
        {
            kept.Add(ordered[0].Name);
        }

        void KeepPer(Func<DateTime, int> bucket, int count)
        {
            var buckets = new HashSet<int>();
            foreach (var (name, createdAt) in ordered)
            {
                if (buckets.Add(bucket(TimeZoneInfo.ConvertTime(createdAt, zone).DateTime)))
                {
                    if (buckets.Count > count)
                    {
                        return;
                    }

                    kept.Add(name);
                }
            }
        }

        KeepPer(d => (d.Year * 1000) + d.DayOfYear, keep.BackupKeepDaily);
        KeepPer(d => (ISOWeek.GetYear(d) * 100) + ISOWeek.GetWeekOfYear(d), keep.BackupKeepWeekly);
        KeepPer(d => (d.Year * 100) + d.Month, keep.BackupKeepMonthly);
        return [.. ordered.Where(i => !kept.Contains(i.Name)).Select(i => i.Name)];
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Configured)
        {
            LogNotConfigured(logger);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException or Npgsql.NpgsqlException)
            {
                LogFailed(logger, exception);
            }

            try
            {
                await Task.Delay(Interval, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static DateTimeOffset CreatedAt(FileInfo file)
    {
        var name = Path.GetFileNameWithoutExtension(file.Name)[Prefix.Length..];
        var stamp = name.Length >= NameFormat.Length ? name[..NameFormat.Length] : name;
        return DateTime.TryParseExact(stamp, NameFormat, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? new DateTimeOffset(parsed, TimeSpan.Zero)
            : new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sicherung {Name} erstellt ({Size} Byte, {Documents} Dateien)")]
    private static partial void LogCreated(ILogger logger, string name, long size, int documents);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sicherung fehlgeschlagen")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kein Sicherungsschlüssel (MYDOCS_BACKUP_KEY) eingerichtet: keine automatische Sicherung.")]
    private static partial void LogNotConfigured(ILogger logger);
}
