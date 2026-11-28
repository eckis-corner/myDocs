using System.Net.Http.Headers;
using System.Net.Http.Json;
using MyDocs.Desktop.Core.Inbox;
using MyDocs.Shared;
using MyDocs.Shared.Documents;
using MyDocs.Shared.Household;

namespace MyDocs.Tests;

/// <summary>Eingangsordner des Desktop-Clients (M4.3).</summary>
public sealed class InboxFolderTests(ServerFixture server) : IClassFixture<ServerFixture>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static int _counter;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "mydocs-inbox-folder", Guid.NewGuid().ToString("N"));
    private HttpClient _admin = null!;
    private HttpClient _bea = null!;
    private string _token = null!;
    private bool _offline;

    public async ValueTask InitializeAsync()
    {
        var n = Interlocked.Increment(ref _counter);
        _admin = await TestAccounts.AdminAsync(server);
        _bea = await TestAccounts.CreateAsync(server, _admin, $"folder-bea{n}@example.test", $"Bea {n}", HouseholdRoles.Member);
        _token = (await DeviceTests.ConnectAsync(server, _bea)).AccessToken;
        Directory.CreateDirectory(_folder);
    }

    public ValueTask DisposeAsync()
    {
        _admin.Dispose();
        _bea.Dispose();
        Directory.Delete(_folder, recursive: true);
        return ValueTask.CompletedTask;
    }

    private sealed class OfflineHandler(Func<bool> offline) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            offline() ? throw new HttpRequestException("Kein Netz") : base.SendAsync(request, cancellationToken);
    }

    private HttpClient Device()
    {
        var client = new HttpClient(new OfflineHandler(() => _offline) { InnerHandler = server.Server.CreateHandler() }) { BaseAddress = new Uri("https://localhost/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        client.DefaultRequestHeaders.Add(ApiHeaders.Csrf, ApiHeaders.CsrfValue);
        return client;
    }

    [Fact]
    public async Task FilesInTheFolderGoToThePrivateInboxAndMoveAside()
    {
        var watcher = new InboxFolderWatcher(Device);
        var uploaded = new List<string>();
        watcher.Uploaded += uploaded.Add;
        var file = Path.Combine(_folder, "Rechnung Heizung.pdf");
        await File.WriteAllTextAsync(file, "%PDF-1.4 Heizung", Ct);
        await File.WriteAllTextAsync(Path.Combine(_folder, "~$Liste.xlsx"), "Sperrdatei", Ct);
        await File.WriteAllTextAsync(Path.Combine(_folder, "Download.pdf.crdownload"), "halb", Ct);

        Assert.Equal(0, await watcher.ScanOnceAsync(_folder, Ct));
        Assert.Equal(1, await watcher.ScanOnceAsync(_folder, Ct));

        Assert.False(File.Exists(file));
        Assert.True(File.Exists(Path.Combine(_folder, InboxFolderWatcher.UploadedFolder, "Rechnung Heizung.pdf")));
        Assert.True(File.Exists(Path.Combine(_folder, "~$Liste.xlsx")));
        Assert.Equal(["Rechnung Heizung.pdf"], uploaded);
        var item = Assert.Single((await _bea.GetFromJsonAsync<List<InboxItemDto>>("/api/inbox", Ct))!, i => i.Title == "Rechnung Heizung");
        Assert.Equal((DocumentSources.Desktop, false), (item.Source, item.Household));
    }

    [Fact]
    public async Task WithoutNetworkTheFileWaits()
    {
        var watcher = new InboxFolderWatcher(Device);
        var file = Path.Combine(_folder, "Brief.pdf");
        await File.WriteAllTextAsync(file, "%PDF-1.4 Brief", Ct);
        _offline = true;

        await watcher.ScanOnceAsync(_folder, Ct);
        await watcher.ScanOnceAsync(_folder, Ct);
        Assert.True(File.Exists(file));

        _offline = false;
        Assert.Equal(1, await watcher.ScanOnceAsync(_folder, Ct));
        Assert.False(File.Exists(file));
    }

    [Theory]
    [InlineData(".DS_Store", true)]
    [InlineData("~$Brief.docx", true)]
    [InlineData("Scan.tmp", true)]
    [InlineData("Thumbs.db", true)]
    [InlineData("Brief.docx", false)]
    public void TemporaryFilesAreLeftAlone(string name, bool ignored) => Assert.Equal(ignored, InboxFolderWatcher.Ignored(name));
}
