using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using MyDocs.Desktop.Core;
using MyDocs.Desktop.Core.Checkout;
using MyDocs.Shared;
using MyDocs.Shared.Audit;
using MyDocs.Shared.Documents;
using MyDocs.Shared.Household;
using MyDocs.Shared.Objects;

namespace MyDocs.Tests;

/// <summary>Check-out und Check-in (M4.2): Sperre, Upload beim Speichern, offline weiterarbeiten, Konfliktversion.</summary>
public sealed class CheckoutTests(ServerFixture server) : IClassFixture<ServerFixture>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static int _counter;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mydocs-checkout-tests", Guid.NewGuid().ToString("N"));
    private HttpClient _admin = null!;
    private HttpClient _bea = null!;
    private HttpClient _carl = null!;
    private Guid _object;
    private string _beaToken = null!;

    /// <summary>Netz an oder aus für den Client von Bea.</summary>
    private bool _offline;

    public async ValueTask InitializeAsync()
    {
        var n = Interlocked.Increment(ref _counter);
        _admin = await TestAccounts.AdminAsync(server);
        _bea = await TestAccounts.CreateAsync(server, _admin, $"co-bea{n}@example.test", $"Bea {n}", HouseholdRoles.Member);
        _carl = await TestAccounts.CreateAsync(server, _admin, $"co-carl{n}@example.test", $"Carl {n}", HouseholdRoles.Member);
        var person = (await TestAccounts.MeAsync(_bea)).PersonId!.Value;
        var data = new ObjectData { Title = "Haushaltsbuch", Fields = new() { ["insuranceKind"] = "liability", ["insurer"] = "HUK" } };
        var created = await _bea.PostAsJsonAsync("/api/objects", new ObjectSaveRequest("insurance", data, null, person), Ct);
        _object = (await created.Content.ReadFromJsonAsync<ObjectDto>(Ct))!.Id;
        _beaToken = (await DeviceTests.ConnectAsync(server, _bea)).AccessToken;
    }

    public ValueTask DisposeAsync()
    {
        _admin.Dispose();
        _bea.Dispose();
        _carl.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    private sealed class OfflineHandler(Func<bool> offline) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            offline() ? throw new HttpRequestException("Kein Netz") : base.SendAsync(request, cancellationToken);
    }

    private HttpClient DeviceClient()
    {
        var client = new HttpClient(new OfflineHandler(() => _offline) { InnerHandler = server.Server.CreateHandler() }) { BaseAddress = new Uri("https://localhost/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _beaToken);
        client.DefaultRequestHeaders.Add(ApiHeaders.Csrf, ApiHeaders.CsrfValue);
        return client;
    }

    private CheckoutEngine Engine() => new(new DesktopPaths(_root), DeviceClient, server.Clock, TimeSpan.FromMilliseconds(200));

    private async Task<DocumentDto> UploadAsync(string content, string fileName = "Liste.xlsx")
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "file", fileName);
        var response = await _bea.PostAsync(new Uri($"/api/objects/{_object}/documents", UriKind.Relative), form, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DocumentDto>(Ct))!;
    }

    private static async Task<HttpResponseMessage> PostVersionAsync(HttpClient client, string path, string content)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "file", "Liste.xlsx");
        return await client.PostAsync(new Uri(path, UriKind.Relative), form, Ct);
    }

    private static async Task<DocumentDto> GetAsync(HttpClient client, Guid id) => (await client.GetFromJsonAsync<DocumentDto>($"/api/documents/{id}", Ct))!;

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 100; i++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100, Ct);
        }

        Assert.Fail("Zustand nicht erreicht.");
    }

    [Fact]
    public async Task ACheckoutLocksTheDocumentForEveryoneElse()
    {
        var document = await UploadAsync("Version 1");
        var checkout = await _bea.PostAsync(new Uri($"/api/documents/{document.Id}/checkout", UriKind.Relative), null, Ct);
        Assert.Equal(HttpStatusCode.OK, checkout.StatusCode);
        Assert.Equal(1, (await checkout.Content.ReadFromJsonAsync<CheckoutDto>(Ct))!.BaseVersion);

        Assert.Equal(HttpStatusCode.Conflict, (await _carl.PostAsync(new Uri($"/api/documents/{document.Id}/checkout", UriKind.Relative), null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostVersionAsync(_carl, $"/api/documents/{document.Id}/versions", "von Carl")).StatusCode);
        var seen = await GetAsync(_carl, document.Id);
        Assert.False(seen.LockedByMe);
        Assert.StartsWith("Bea", seen.LockedByName, StringComparison.Ordinal);
        Assert.True((await GetAsync(_bea, document.Id)).LockedByMe);

        Assert.Equal(HttpStatusCode.NoContent, (await _bea.PutAsync(new Uri($"/api/documents/{document.Id}/checkout", UriKind.Relative), null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _carl.PutAsync(new Uri($"/api/documents/{document.Id}/checkout", UriKind.Relative), null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _carl.DeleteAsync($"/api/documents/{document.Id}/checkout", Ct)).StatusCode);

        // Der Administrator hebt die Sperre auf; das steht im Protokoll.
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/documents/{document.Id}/checkout", Ct)).StatusCode);
        Assert.Null((await GetAsync(_carl, document.Id)).LockedByName);
        var audit = (await _bea.GetFromJsonAsync<List<AuditEntryDto>>("/api/audit", Ct))!;
        Assert.Contains(audit, a => a.Action == AuditActions.DocumentCheckedOut && a.DocumentId == document.Id);
    }

    [Fact]
    public async Task ALockEndsAfterADayWithoutSignOfLife()
    {
        var document = await UploadAsync("Version 1");
        Assert.Equal(HttpStatusCode.OK, (await _bea.PostAsync(new Uri($"/api/documents/{document.Id}/checkout", UriKind.Relative), null, Ct)).StatusCode);

        server.Clock.Advance(CheckoutRules.LockLifetime + TimeSpan.FromMinutes(1));
        using var carl = await TestAccounts.AdminAsync(server);

        Assert.Equal(HttpStatusCode.OK, (await carl.PostAsync(new Uri($"/api/documents/{document.Id}/checkout", UriKind.Relative), null, Ct)).StatusCode);
    }

    [Fact]
    public async Task EverySaveBecomesAVersionAndCheckInCleansUp()
    {
        var document = await UploadAsync("Version 1");
        using var engine = Engine();

        var entry = await engine.CheckOutAsync(document.Id, Ct);
        Assert.Equal("Version 1", await File.ReadAllTextAsync(entry.Path, Ct));
        Assert.True((await GetAsync(_bea, document.Id)).LockedByMe);

        await File.WriteAllTextAsync(entry.Path, "Version 2 aus Excel", Ct);
        await WaitUntilAsync(async () => (await GetAsync(_bea, document.Id)).Versions.Count == 2);
        Assert.Equal(2, engine.Entries.Single().BaseVersion);
        Assert.Equal("Version 2 aus Excel", await _bea.GetStringAsync($"/api/documents/{document.Id}/content", Ct));

        await File.WriteAllTextAsync(entry.Path, "Version 3", Ct);
        await engine.CheckInAsync(document.Id, Ct);

        var after = await GetAsync(_bea, document.Id);
        Assert.Equal(3, after.Versions.Count);
        Assert.Null(after.LockedByName);
        Assert.Empty(engine.Entries);
        Assert.False(Directory.Exists(Path.GetDirectoryName(entry.Path)));
    }

    [Fact]
    public async Task OfflineChangesWaitAndGoUpLater()
    {
        var document = await UploadAsync("Version 1");
        using var engine = Engine();
        var entry = await engine.CheckOutAsync(document.Id, Ct);

        _offline = true;
        await File.WriteAllTextAsync(entry.Path, "im Zug bearbeitet", Ct);
        await WaitUntilAsync(() => Task.FromResult(engine.Entries.Single().Pending));
        await Assert.ThrowsAsync<CheckInBlockedException>(() => engine.CheckInAsync(document.Id, Ct));

        // Neustart ohne Netz: Der Stand übersteht ihn.
        engine.Dispose();
        using var restarted = Engine();
        _offline = false;
        await restarted.ResumeAsync(Ct);

        Assert.False(restarted.Entries.Single().Pending);
        Assert.Equal("im Zug bearbeitet", await _bea.GetStringAsync($"/api/documents/{document.Id}/content", Ct));
    }

    [Fact]
    public async Task AChangeMeanwhileGivesAConflictDocumentInsteadOfOverwriting()
    {
        var document = await UploadAsync("Version 1");
        using var engine = Engine();
        var entry = await engine.CheckOutAsync(document.Id, Ct);
        var notices = new List<CheckoutNotice>();
        engine.Notice += notices.Add;

        // Sperre aufgehoben, Carl lädt eine eigene Version hoch.
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/documents/{document.Id}/checkout", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _carl.PostAsync(new Uri($"/api/documents/{document.Id}/checkout", UriKind.Relative), null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostVersionAsync(_carl, $"/api/documents/{document.Id}/checkout/versions?baseVersion=1", "Carls Fassung")).StatusCode);

        await File.WriteAllTextAsync(entry.Path, "Beas Fassung", Ct);
        await engine.SyncAsync(document.Id, Ct);

        var current = engine.Entries.Single();
        Assert.True(current.Conflict);
        Assert.NotEqual(document.Id, current.DocumentId);
        Assert.Contains(notices, n => n.Kind == CheckoutNoticeKind.Conflict);
        Assert.Equal("Carls Fassung", await _bea.GetStringAsync($"/api/documents/{document.Id}/content", Ct));
        var conflict = await GetAsync(_bea, current.DocumentId);
        Assert.Contains("(Konflikt", conflict.Title, StringComparison.Ordinal);
        Assert.Equal([_object], conflict.ObjectIds);
        Assert.True(conflict.LockedByMe);
        Assert.Equal("Beas Fassung", await _bea.GetStringAsync($"/api/documents/{current.DocumentId}/content", Ct));

        // Weitere Speicherungen gehen an das Konfliktdokument.
        await File.WriteAllTextAsync(entry.Path, "Beas Fassung, ergänzt", Ct);
        await WaitUntilAsync(async () => (await GetAsync(_bea, current.DocumentId)).Versions.Count == 2);
    }
}
