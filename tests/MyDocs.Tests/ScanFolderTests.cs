using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MyDocs.Server.Infrastructure.Inbox;
using MyDocs.Shared.Documents;
using MyDocs.Shared.Household;

namespace MyDocs.Tests;

/// <summary>Scan-Ordner (M3.5): Haushalt und Unterordner je Konto, erst nach Ende des Schreibens, danach gelöscht.</summary>
public sealed class ScanFolderTests(ServerFixture server) : IClassFixture<ServerFixture>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static int _counter;
    private HttpClient _admin = null!;
    private HttpClient _bea = null!;
    private string _beaEmail = null!;

    public async ValueTask InitializeAsync()
    {
        var n = Interlocked.Increment(ref _counter);
        _admin = await TestAccounts.AdminAsync(server);
        _beaEmail = $"scan-bea{n}@example.test";
        _bea = await TestAccounts.CreateAsync(server, _admin, _beaEmail, $"Bea {n}", HouseholdRoles.Member);
        Directory.CreateDirectory(server.ScanDirectory);
    }

    public ValueTask DisposeAsync()
    {
        _admin.Dispose();
        _bea.Dispose();
        return ValueTask.CompletedTask;
    }

    private ScanFolderService Scanner => server.Services.GetRequiredService<ScanFolderService>();

    private static async Task<List<InboxItemDto>> InboxAsync(HttpClient client) => (await client.GetFromJsonAsync<List<InboxItemDto>>("/api/inbox", Ct))!;

    [Fact]
    public async Task FilesInTheFolderGoToTheHouseholdInboxOnceTheyAreComplete()
    {
        var file = Path.Combine(server.ScanDirectory, $"Scan_{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(file, "%PDF-1.4 SCAN Nebenkostenabrechnung", Ct);

        await Scanner.ScanOnceAsync(Ct);
        // Noch nicht fertig geschrieben: Der Scanner hängt weitere Seiten an.
        await File.AppendAllTextAsync(file, " Seite 2", Ct);
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(5));
        await Scanner.ScanOnceAsync(Ct);
        Assert.True(File.Exists(file));

        await Scanner.ScanOnceAsync(Ct);

        Assert.False(File.Exists(file));
        var item = Assert.Single(await InboxAsync(_bea), i => i.SourceDetail == Path.GetFileName(file));
        Assert.Equal((DocumentSources.Scan, true), (item.Source, item.Household));
        var content = await _bea.GetStringAsync($"/api/documents/{item.Id}/content", Ct);
        Assert.Equal("%PDF-1.4 SCAN Nebenkostenabrechnung Seite 2", content);
    }

    [Fact]
    public async Task AFolderNamedLikeAnAccountGoesToItsPrivateInbox()
    {
        var folder = Path.Combine(server.ScanDirectory, _beaEmail.ToUpperInvariant());
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "Foto.jpg");
        await File.WriteAllBytesAsync(file, TestImages.RgbJpeg, Ct);

        await Scanner.ScanOnceAsync(Ct);
        await Scanner.ScanOnceAsync(Ct);

        Assert.False(File.Exists(file));
        var item = Assert.Single(await InboxAsync(_bea), i => i.Source == DocumentSources.Scan && !i.Household);
        Assert.Equal("image/jpeg", item.Current.ContentType);
        Assert.DoesNotContain(await InboxAsync(_admin), i => i.Id == item.Id);
    }

    [Fact]
    public async Task OtherFilesAndUnknownFoldersAreNotTaken()
    {
        var text = Path.Combine(server.ScanDirectory, $"notiz-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(text, "kein Scan", Ct);
        var unknown = Path.Combine(server.ScanDirectory, "jemand@anders.test");
        Directory.CreateDirectory(unknown);
        var stray = Path.Combine(unknown, "Scan.pdf");
        await File.WriteAllTextAsync(stray, "%PDF-1.4 SCAN", Ct);
        var hidden = Path.Combine(server.ScanDirectory, ".DS_Store");
        await File.WriteAllTextAsync(hidden, "x", Ct);

        await Scanner.ScanOnceAsync(Ct);
        await Scanner.ScanOnceAsync(Ct);

        Assert.False(File.Exists(text));
        Assert.True(File.Exists(Path.Combine(server.ScanDirectory, ScanFolderService.ErrorFolder, Path.GetFileName(text))));
        Assert.True(File.Exists(stray));
        Assert.True(File.Exists(hidden));
        Assert.DoesNotContain(await InboxAsync(_admin), i => i.Title.Contains("notiz", StringComparison.Ordinal));
        Assert.True(Scanner.IsActive);
    }
}
