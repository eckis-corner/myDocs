using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure;
using MyDocs.Server.Infrastructure.Backup;
using MyDocs.Server.Infrastructure.Files;
using MyDocs.Shared.Admin;
using MyDocs.Shared.Audit;
using MyDocs.Shared.Documents;
using MyDocs.Shared.Household;
using MyDocs.Shared.Objects;

namespace MyDocs.Tests;

/// <summary>Sicherung, Rücksicherung und Hauptschlüssel (M1.7b).</summary>
public sealed class BackupTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ABackupBringsBackEntriesDocumentsAndKeys()
    {
        using var admin = await TestAccounts.AdminAsync(server);
        var person = (await TestAccounts.MeAsync(admin)).PersonId!.Value;
        var marker = "Rücksicherung-" + Guid.NewGuid().ToString("N")[..8];
        var created = await admin.PostAsJsonAsync("/api/objects", new ObjectSaveRequest("contract",
            new ObjectData { Title = marker, Fields = new() { ["contractKind"] = "internet", ["provider"] = "Glasfaser AG" } }, null, person), Ct);
        var item = (await created.Content.ReadFromJsonAsync<ObjectDto>(Ct))!;
        using var form = new MultipartFormDataContent();
        var content = Encoding.UTF8.GetBytes("Vertragsinhalt " + marker);
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "vertrag.pdf");
        var document = (await (await admin.PostAsync(new Uri($"/api/objects/{item.Id}/documents", UriKind.Relative), form, Ct)).Content.ReadFromJsonAsync<DocumentDto>(Ct))!;

        var run = await admin.PostAsync(new Uri("/api/admin/backup", UriKind.Relative), null, Ct);
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        var backup = (await run.Content.ReadFromJsonAsync<BackupItemDto>(Ct))!;
        var path = Path.Combine(server.DataDirectory, "backup", backup.Name);
        var bytes = await File.ReadAllBytesAsync(path, Ct);
        Assert.Equal("MYDOCSBK"u8.ToArray(), bytes[..8]);
        // Verschlüsselt: weder Titel noch Dateiinhalt stehen im Klartext darin.
        Assert.Equal(-1, bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(marker)));
        var status = (await admin.GetFromJsonAsync<BackupStatusDto>("/api/admin/backup", Ct))!;
        Assert.True(status.Configured);
        Assert.Contains(status.Items, i => i.Name == backup.Name);

        // Daten verlieren: Eintrag weg, Datei weg, Hauptschlüssel weg.
        string hash;
        using (var scope = server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyDocsDbContext>();
            hash = await db.DocumentVersions.Where(v => v.DocumentId == document.Id).Select(v => v.BlobHash).SingleAsync(Ct);
            await db.Objects.Where(o => o.Id == item.Id).ExecuteDeleteAsync(Ct);
        }

        var blobPath = Path.Combine(server.DataDirectory, "files", hash[..2], hash);
        File.Delete(blobPath);
        var keyPath = Path.Combine(server.DataDirectory, "keys", "master.key");
        var originalKey = await File.ReadAllBytesAsync(keyPath, Ct);
        File.Delete(keyPath);

        var manifest = await BackupArchive.RestoreAsync(path, server.ConnectionString, server.DataDirectory, BackupFormat.ParseKey(ServerFixture.BackupKey)!, Ct);

        Assert.True(manifest.Documents >= 1 && manifest.Keys >= 2);
        Assert.Equal(originalKey, await File.ReadAllBytesAsync(keyPath, Ct));
        Assert.True(File.Exists(blobPath));
        using (var scope = server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyDocsDbContext>();
            Assert.Equal(marker, (await db.Objects.SingleAsync(o => o.Id == item.Id, Ct)).Title);
            Assert.True(await db.AuditLog.AnyAsync(a => a.Action == AuditActions.BackupCreated, Ct));
        }

        // Und der Inhalt lässt sich wieder entschlüsseln.
        using var again = await TestAccounts.AdminAsync(server);
        Assert.Equal(content, await again.GetByteArrayAsync(new Uri($"/api/documents/{document.Id}/content", UriKind.Relative), Ct));
    }

    [Fact]
    public async Task AWrongBackupKeyIsRejected()
    {
        using var admin = await TestAccounts.AdminAsync(server);
        var backup = (await (await admin.PostAsync(new Uri("/api/admin/backup", UriKind.Relative), null, Ct)).Content.ReadFromJsonAsync<BackupItemDto>(Ct))!;
        var wrong = new byte[32];

        await Assert.ThrowsAnyAsync<System.Security.Cryptography.CryptographicException>(() =>
            BackupArchive.RestoreAsync(Path.Combine(server.DataDirectory, "backup", backup.Name), server.ConnectionString, server.DataDirectory, wrong, Ct));
    }

    [Fact]
    public async Task TheMasterKeyIsShownOnlyWithAFreshCodeAndComesBackFromTheWords()
    {
        var account = await server.EnsureAdminAsync();
        using var admin = await TestAccounts.AdminAsync(server);

        var wrong = await admin.PostAsJsonAsync("/api/admin/master-key", new MasterKeyRequest("000000"), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);

        var shown = await admin.PostAsJsonAsync("/api/admin/master-key", new MasterKeyRequest(server.NextTotp(account.TotpSecret)), Ct);
        var words = (await shown.Content.ReadFromJsonAsync<MasterKeyDto>(Ct))!.Words;
        Assert.Equal(24, words.Count);

        var restored = Path.Combine(Path.GetTempPath(), "mydocs-key-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.True(MasterKey.Restore(restored, string.Join(' ', words)));
            Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(server.DataDirectory, "keys", "master.key"), Ct),
                await File.ReadAllBytesAsync(Path.Combine(restored, "keys", "master.key"), Ct));
            Assert.False(MasterKey.Restore(restored, string.Join(' ', words.Reverse())));
        }
        finally
        {
            Directory.Delete(restored, recursive: true);
        }

        using var log = server.Services.CreateScope();
        Assert.True(await log.ServiceProvider.GetRequiredService<MyDocsDbContext>().AuditLog.AnyAsync(a => a.Action == AuditActions.MasterKeyShown, Ct));
    }

    [Fact]
    public async Task OnlyAdministratorsReachBackupAndMasterKey()
    {
        using var admin = await TestAccounts.AdminAsync(server);
        using var member = await TestAccounts.CreateAsync(server, admin, $"backup-{Guid.NewGuid():N}@example.test", "Mitglied", HouseholdRoles.Member);

        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync(new Uri("/api/admin/backup", UriKind.Relative), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/admin/master-key", new MasterKeyRequest("123456"), Ct)).StatusCode);
    }

    [Fact]
    public void GenerationsKeepDaysWeeksAndMonths()
    {
        var now = new DateTimeOffset(2026, 11, 27, 3, 0, 0, TimeSpan.Zero);
        var items = Enumerable.Range(0, 400).Select(d => ($"b{d}", now.AddDays(-d))).ToList();

        var delete = BackupService.ToDelete(items, new MyDocsOptions { BackupKeepDaily = 7, BackupKeepWeekly = 4, BackupKeepMonthly = 6 }, TimeZoneInfo.Utc);

        var kept = items.Select(i => i.Item1).Except(delete).ToList();
        Assert.Contains("b0", kept);
        Assert.All(Enumerable.Range(0, 7), d => Assert.Contains($"b{d}", kept));
        Assert.InRange(kept.Count, 7, 7 + 4 + 6);
        Assert.Contains("b399", delete);
    }

    [Fact]
    public async Task TheCommandLineNeedsConfirmation()
    {
        var options = new MyDocsOptions { DataDirectory = server.DataDirectory, BackupKey = ServerFixture.BackupKey };
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();

        Assert.True(BackupCli.Handles(["restore", "x.mdbak"]));
        Assert.False(BackupCli.Handles(["run"]));
        Assert.Equal(2, await BackupCli.RunAsync(["restore", "x.mdbak"], configuration, options));
        Assert.Equal(1, await BackupCli.RunAsync(["master-key", "restore", "kein gültiger schlüssel", "--confirm"], configuration, options));
    }
}
