using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyDocs.Server.Data;
using MyDocs.Shared.Admin;

namespace MyDocs.Server.Infrastructure.Settings;

/// <summary>
/// Einstellungen des Administrators als ein JSON-Wert in <c>system_settings</c> (wie myLife). SMTP-Passwort und
/// ntfy-Token liegen darin mit Data Protection verschlüsselt und verlassen den Server nie. Bis zum ersten Speichern
/// gelten die Werte aus der Umgebung. Gespeicherte Werte für Dateigröße und Sicherung schreibt der Speicher in die
/// <see cref="MyDocsOptions"/>, die Upload und Sicherung bei jedem Lauf lesen; die Dateigröße bleibt dabei unter der
/// Grenze aus der Umgebung, die beim Start die Größe der Anfragen festlegt.
/// </summary>
public sealed class SettingsStore : IDisposable
{
    internal const string SettingKey = "admin:settings";
    private const string SmtpPurpose = "MyDocs.Settings.SmtpPassword.v1";
    private const string NtfyPurpose = "MyDocs.Settings.NtfyToken.v1";
    private const string ImapPurpose = "MyDocs.Settings.ImapPassword.v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IServiceScopeFactory _scopes;
    private readonly IDataProtectionProvider _protection;
    private readonly MyDocsOptions _options;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private AdminSettingsDto? _cache;

    public SettingsStore(IServiceScopeFactory scopes, IDataProtectionProvider protection, IOptions<MyDocsOptions> options, TimeProvider time)
    {
        _scopes = scopes;
        _protection = protection;
        _options = options.Value;
        _time = time;
        FileSizeCeiling = _options.MaxFileSizeMegabytes;
        Defaults = new AdminSettingsDto
        {
            MaxFileSizeMegabytes = _options.MaxFileSizeMegabytes,
            FileSizeCeilingMegabytes = FileSizeCeiling,
            Backup = new BackupSettingsDto
            {
                Hour = _options.BackupHour,
                KeepDaily = _options.BackupKeepDaily,
                KeepWeekly = _options.BackupKeepWeekly,
                KeepMonthly = _options.BackupKeepMonthly,
                IncludeDocuments = _options.BackupIncludeDocuments,
            },
        };
    }

    /// <summary>Größte erlaubte Dateigröße aus der Umgebung.</summary>
    public int FileSizeCeiling { get; }

    private AdminSettingsDto Defaults { get; }

    /// <summary>Gespeicherte Form: Geheimnisse noch verschlüsselt.</summary>
    public async Task<AdminSettingsDto> GetAsync(CancellationToken ct = default)
    {
        if (_cache is { } cached)
        {
            return cached;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (_cache is null)
            {
                await using var scope = _scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<MyDocsDbContext>();
                var setting = await db.SystemSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Key == SettingKey, ct);
                _cache = setting is null ? Defaults : (JsonSerializer.Deserialize<AdminSettingsDto>(setting.Value, Json) ?? Defaults) with { FileSizeCeilingMegabytes = FileSizeCeiling };
                Apply(_cache);
            }

            return _cache;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Für die Oberfläche: ohne Geheimnisse, nur ob welche hinterlegt sind.</summary>
    public async Task<AdminSettingsDto> GetForAdminAsync(CancellationToken ct = default)
    {
        var stored = await GetAsync(ct);
        return stored with
        {
            Smtp = stored.Smtp with { Password = null, HasPassword = stored.Smtp.Password is not null },
            Ntfy = stored.Ntfy with { AccessToken = null, HasAccessToken = stored.Ntfy.AccessToken is not null },
            Imap = stored.Imap with { Password = null, HasPassword = stored.Imap.Password is not null },
        };
    }

    /// <summary>Speichert geprüfte Einstellungen. Geheimnisse: null behält den alten Wert, leer entfernt ihn.</summary>
    public async Task SaveAsync(MyDocsDbContext db, AdminSettingsDto incoming, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(incoming);
        var current = await GetAsync(ct);
        var normalized = Normalize(incoming);
        var stored = normalized with
        {
            Smtp = normalized.Smtp with { Password = Secret(incoming.Smtp.Password, current.Smtp.Password, SmtpPurpose), HasPassword = false },
            Ntfy = normalized.Ntfy with { AccessToken = Secret(incoming.Ntfy.AccessToken, current.Ntfy.AccessToken, NtfyPurpose), HasAccessToken = false },
            Imap = normalized.Imap with { Password = Secret(incoming.Imap?.Password, current.Imap.Password, ImapPurpose), HasPassword = false },
            FileSizeCeilingMegabytes = FileSizeCeiling,
        };

        var value = JsonSerializer.Serialize(stored, Json);
        var setting = await db.SystemSettings.SingleOrDefaultAsync(s => s.Key == SettingKey, ct);
        if (setting is null)
        {
            db.SystemSettings.Add(new SystemSetting { Key = SettingKey, Value = value, UpdatedAt = _time.GetUtcNow() });
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAt = _time.GetUtcNow();
        }

        await db.SaveChangesAsync(ct);
        _cache = stored;
        Apply(stored);
    }

    public string? SmtpPassword(AdminSettingsDto stored) => Reveal(stored?.Smtp.Password, SmtpPurpose);

    public string? NtfyToken(AdminSettingsDto stored) => Reveal(stored?.Ntfy.AccessToken, NtfyPurpose);

    public string? ImapPassword(AdminSettingsDto stored) => Reveal(stored?.Imap.Password, ImapPurpose);

    public static bool IsImapConfigured(AdminSettingsDto settings) =>
        !string.IsNullOrWhiteSpace(settings?.Imap.Host) && !string.IsNullOrWhiteSpace(settings.Imap.Username);

    public static bool IsMailConfigured(AdminSettingsDto settings) =>
        !string.IsNullOrWhiteSpace(settings?.Smtp.Host) && !string.IsNullOrWhiteSpace(settings.Smtp.FromAddress);

    public static bool IsNtfyConfigured(AdminSettingsDto settings) => !string.IsNullOrWhiteSpace(settings?.Ntfy.ServerUrl);

    /// <summary>Verwirft den Zwischenspeicher, etwa nach einer Rücksicherung.</summary>
    public void Invalidate() => _cache = null;

    public void Dispose() => _lock.Dispose();

    private void Apply(AdminSettingsDto settings)
    {
        _options.MaxFileSizeMegabytes = Math.Clamp(settings.MaxFileSizeMegabytes, 1, FileSizeCeiling);
        _options.BackupHour = Math.Clamp(settings.Backup.Hour, 0, 23);
        _options.BackupKeepDaily = settings.Backup.KeepDaily;
        _options.BackupKeepWeekly = settings.Backup.KeepWeekly;
        _options.BackupKeepMonthly = settings.Backup.KeepMonthly;
        _options.BackupIncludeDocuments = settings.Backup.IncludeDocuments;
    }

    private static AdminSettingsDto Normalize(AdminSettingsDto settings) => settings with
    {
        PublicUrl = string.IsNullOrWhiteSpace(settings.PublicUrl) ? null : settings.PublicUrl.Trim().TrimEnd('/'),
        Smtp = (settings.Smtp ?? new SmtpSettingsDto()) with
        {
            Host = settings.Smtp?.Host?.Trim() ?? string.Empty,
            Username = string.IsNullOrWhiteSpace(settings.Smtp?.Username) ? null : settings.Smtp.Username.Trim(),
            FromAddress = settings.Smtp?.FromAddress?.Trim() ?? string.Empty,
            FromName = string.IsNullOrWhiteSpace(settings.Smtp?.FromName) ? "myDocs" : settings.Smtp.FromName.Trim(),
        },
        Ntfy = (settings.Ntfy ?? new NtfySettingsDto()) with
        {
            ServerUrl = string.IsNullOrWhiteSpace(settings.Ntfy?.ServerUrl) ? null : settings.Ntfy.ServerUrl.Trim().TrimEnd('/'),
        },
        Imap = (settings.Imap ?? new ImapSettingsDto()) with
        {
            Host = settings.Imap?.Host?.Trim() ?? string.Empty,
            Username = string.IsNullOrWhiteSpace(settings.Imap?.Username) ? null : settings.Imap.Username.Trim(),
            Folder = string.IsNullOrWhiteSpace(settings.Imap?.Folder) ? "INBOX" : settings.Imap.Folder.Trim(),
            TrustedAuthServId = string.IsNullOrWhiteSpace(settings.Imap?.TrustedAuthServId) ? null : settings.Imap.TrustedAuthServId.Trim(),
        },
        Backup = settings.Backup ?? new BackupSettingsDto(),
    };

    private string? Secret(string? incoming, string? currentProtected, string purpose) => incoming switch
    {
        null => currentProtected,
        "" => null,
        _ => _protection.CreateProtector(purpose).Protect(incoming),
    };

    private string? Reveal(string? protectedValue, string purpose)
    {
        if (protectedValue is null)
        {
            return null;
        }

        try
        {
            return _protection.CreateProtector(purpose).Unprotect(protectedValue);
        }
        catch (CryptographicException)
        {
            // Schlüsselring verloren (Rücksicherung ohne data/app/keys): wie nicht hinterlegt.
            return null;
        }
    }
}
