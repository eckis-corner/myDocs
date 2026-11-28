using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Tasks;
using MyDocs.Shared;
using MyDocs.Shared.Documents;
using MyDocs.Shared.Household;
using MyDocs.Shared.MyLife;
using MyDocs.Shared.Objects;
using MyDocs.Shared.Tasks;

namespace MyDocs.Tests;

/// <summary>Brücke zu myLife (M5.1): lesendes Token, Umfang je Person, Dokumente, CORS, Quittung und Vorgang.</summary>
public sealed class MyLifeBridgeTests(ServerFixture server) : IClassFixture<ServerFixture>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static int _counter;
    private HttpClient _admin = null!;
    private HttpClient _bea = null!;
    private HttpClient _carl = null!;
    private Guid _beaPerson;
    private Guid _carlPerson;

    public async ValueTask InitializeAsync()
    {
        var n = Interlocked.Increment(ref _counter);
        _admin = await TestAccounts.AdminAsync(server);
        _bea = await TestAccounts.CreateAsync(server, _admin, $"ml-bea{n}@example.test", $"Bea {n}", HouseholdRoles.Member);
        _carl = await TestAccounts.CreateAsync(server, _admin, $"ml-carl{n}@example.test", $"Carl {n}", HouseholdRoles.Member);
        _beaPerson = (await TestAccounts.MeAsync(_bea)).PersonId!.Value;
        _carlPerson = (await TestAccounts.MeAsync(_carl)).PersonId!.Value;
    }

    public ValueTask DisposeAsync()
    {
        _admin.Dispose();
        _bea.Dispose();
        _carl.Dispose();
        return ValueTask.CompletedTask;
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string type, string title, Dictionary<string, string> fields, Guid? owner, Guid? responsible,
        Dictionary<string, Guid>? locations = null)
    {
        var data = new ObjectData { Title = title, Fields = fields, Locations = locations ?? [] };
        var response = await client.PostAsJsonAsync("/api/objects", new ObjectSaveRequest(type, data, owner, responsible), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ObjectDto>(Ct))!.Id;
    }

    private static async Task<Guid> UploadAsync(HttpClient client, Guid objectId, byte[] content, string fileName = "Police.pdf")
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(content), "file", fileName);
        var response = await client.PostAsync(new Uri($"/api/objects/{objectId}/documents", UriKind.Relative), form, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DocumentDto>(Ct))!.Id;
    }

    private static async Task<string> ConnectAsync(HttpClient client)
    {
        var response = await client.PostAsync(new Uri("/api/account/mylife", UriKind.Relative), null, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<MyLifeConnectResult>(Ct))!;
        Assert.StartsWith(MyLifeRules.TokenPrefix, result.Token, StringComparison.Ordinal);
        Assert.Equal("https://localhost/", result.ServerUrl);
        Assert.StartsWith("https://mylife.example.test/mydocs#server=", result.ConnectUrl, StringComparison.Ordinal);
        return result.Token;
    }

    /// <summary>Wie myLife: ohne Cookie, nur mit dem Token.</summary>
    private HttpClient Bridge(string? token)
    {
        var client = server.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        client.DefaultRequestHeaders.Add(ApiHeaders.Csrf, ApiHeaders.CsrfValue);
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private async Task<BridgeExport> ExportAsync(string token)
    {
        using var client = Bridge(token);
        return (await client.GetFromJsonAsync<BridgeExport>("/api/mylife/export", Ct))!;
    }

    [Fact]
    public async Task TheExportHoldsOwnAndResponsibleEntriesWithMarkedDocumentsOnly()
    {
        var shelf = await CreateAsync(_carl, "location", "Ordner Steuern", [], null, _carlPerson);
        var carlsAccount = await CreateAsync(_carl, "bankAccount", "Konto Carl", new() { ["bank"] = "Sparkasse" }, null, _carlPerson);
        var insurance = await CreateAsync(_bea, "insurance", "Haftpflicht", new() { ["insuranceKind"] = "liability", ["insurer"] = "HUK", ["paymentAccount"] = carlsAccount.ToString() }, _beaPerson, _beaPerson);
        var taxes = await CreateAsync(_bea, "taxes", "Steuern", new() { ["taxOffice"] = "FA Mitte", ["recordsLocation"] = "Ordner Steuern" }, null, _beaPerson,
            new() { ["recordsLocation"] = shelf });
        var carlsContract = await CreateAsync(_carl, "contract", "Handy Carl", new() { ["contractKind"] = "rent", ["provider"] = "X" }, null, _carlPerson);
        var hidden = await CreateAsync(_bea, "contract", "Nur myDocs", new() { ["contractKind"] = "rent", ["provider"] = "Y" }, _beaPerson, _beaPerson);
        Assert.Equal(HttpStatusCode.NoContent, (await _bea.PutAsJsonAsync($"/api/objects/{hidden}/mylife", new MyLifeFlagRequest(false), Ct)).StatusCode);
        Assert.False((await _bea.GetFromJsonAsync<ObjectDto>($"/api/objects/{hidden}", Ct))!.MyLife);

        var marked = await UploadAsync(_bea, insurance, Encoding.UTF8.GetBytes("%PDF Police"));
        await UploadAsync(_bea, insurance, Encoding.UTF8.GetBytes("%PDF Rechnung"), "Rechnung.pdf");
        Assert.Equal(HttpStatusCode.NoContent, (await _bea.PutAsJsonAsync($"/api/documents/{marked}/mylife", new MyLifeFlagRequest(true), Ct)).StatusCode);
        // Fremde private Einträge und Dokumente lassen sich nicht markieren.
        Assert.Equal(HttpStatusCode.NotFound, (await _carl.PutAsJsonAsync($"/api/objects/{insurance}/mylife", new MyLifeFlagRequest(false), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _carl.PutAsJsonAsync($"/api/documents/{marked}/mylife", new MyLifeFlagRequest(false), Ct)).StatusCode);

        var export = await ExportAsync(await ConnectAsync(_bea));

        Assert.Equal(BridgeExport.FormatName, export.Format);
        Assert.Equal(1, export.Version);
        Assert.Equal(_beaPerson, export.Person.Id);
        Assert.Equal(new[] { insurance, taxes, shelf }.Order(), export.Objects.Select(o => o.Id).Order());
        Assert.DoesNotContain(export.Objects, o => o.Id == carlsContract || o.Id == hidden || o.Id == carlsAccount);
        var policy = export.Objects.Single(o => o.Id == insurance);
        Assert.False(policy.Fields.ContainsKey("paymentAccount"));
        Assert.Equal("HUK", policy.Fields["insurer"]);
        Assert.Equal("active", policy.LifecycleState);
        Assert.False(policy.Ended);
        var document = Assert.Single(policy.Documents);
        Assert.Equal(marked, document.Id);
        Assert.Equal("Police.pdf", document.FileName);
        Assert.Equal(64, document.Hash.Length);
        Assert.Equal(shelf, export.Objects.Single(o => o.Id == taxes).Locations["recordsLocation"]);

        // Der Fingerabdruck hängt am Inhalt, nicht an Notizen oder der Uhrzeit.
        var again = await ExportAsync(await ConnectAsync(_bea));
        Assert.Equal(export.Fingerprint, again.Fingerprint);
        var status = (await _bea.GetFromJsonAsync<MyLifeLinkDto>("/api/account/mylife", Ct))!;
        Assert.True(status.Connected);
        Assert.True(status.Pending);
        Assert.Equal(3, status.Objects.Count);
        Assert.Equal("https://mylife.example.test/", status.MyLifeUrl);
    }

    [Fact]
    public async Task TheTokenOpensOnlyTheBridge()
    {
        var token = await ConnectAsync(_bea);
        using (var bridge = Bridge(token))
        {
            Assert.Equal(HttpStatusCode.OK, (await bridge.GetAsync(new Uri("/api/mylife/export", UriKind.Relative), Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await bridge.GetAsync(new Uri("/api/objects", UriKind.Relative), Ct)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await bridge.GetAsync(new Uri("/api/auth/me", UriKind.Relative), Ct)).StatusCode);
        }

        // Die Sitzung im Browser ist kein Token für die Brücke, ein falsches Token auch nicht.
        Assert.Equal(HttpStatusCode.Unauthorized, (await _bea.GetAsync(new Uri("/api/mylife/export", UriKind.Relative), Ct)).StatusCode);
        using (var wrong = Bridge(MyLifeRules.TokenPrefix + "falsch"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await wrong.GetAsync(new Uri("/api/mylife/export", UriKind.Relative), Ct)).StatusCode);
        }

        // Ein neues Token ersetzt das alte; Trennen beendet beide.
        var second = await ConnectAsync(_bea);
        using (var old = Bridge(token))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await old.GetAsync(new Uri("/api/mylife/export", UriKind.Relative), Ct)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await _bea.DeleteAsync("/api/account/mylife", Ct)).StatusCode);
        using (var revoked = Bridge(second))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await revoked.GetAsync(new Uri("/api/mylife/export", UriKind.Relative), Ct)).StatusCode);
        }

        // Ein gesperrtes Konto liest auch über das Token nichts mehr.
        var third = await ConnectAsync(_bea);
        var me = await TestAccounts.MeAsync(_bea);
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.PostAsync(new Uri($"/api/admin/users/{me.Id}/lock", UriKind.Relative), null, Ct)).StatusCode);
        using var locked = Bridge(third);
        Assert.Equal(HttpStatusCode.Unauthorized, (await locked.GetAsync(new Uri("/api/mylife/export", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task DocumentsComeAsBytesOnlyWhenMarkedAndAtMost25Megabytes()
    {
        var insurance = await CreateAsync(_bea, "insurance", "Hausrat", new() { ["insuranceKind"] = "household", ["insurer"] = "Allianz" }, _beaPerson, _beaPerson);
        var marked = await UploadAsync(_bea, insurance, Encoding.UTF8.GetBytes("%PDF Hausrat"));
        var unmarked = await UploadAsync(_bea, insurance, Encoding.UTF8.GetBytes("%PDF Brief"), "Brief.pdf");
        var carls = await CreateAsync(_carl, "insurance", "Carls", new() { ["insuranceKind"] = "liability", ["insurer"] = "X" }, _carlPerson, _carlPerson);
        var carlsDocument = await UploadAsync(_carl, carls, Encoding.UTF8.GetBytes("%PDF Carl"));
        Assert.Equal(HttpStatusCode.NoContent, (await _bea.PutAsJsonAsync($"/api/documents/{marked}/mylife", new MyLifeFlagRequest(true), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _carl.PutAsJsonAsync($"/api/documents/{carlsDocument}/mylife", new MyLifeFlagRequest(true), Ct)).StatusCode);

        using var bridge = Bridge(await ConnectAsync(_bea));
        Assert.Equal("%PDF Hausrat", await bridge.GetStringAsync($"/api/mylife/documents/{marked}/content", Ct));
        Assert.Equal(HttpStatusCode.NotFound, (await bridge.GetAsync(new Uri($"/api/mylife/documents/{unmarked}/content", UriKind.Relative), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bridge.GetAsync(new Uri($"/api/mylife/documents/{carlsDocument}/content", UriKind.Relative), Ct)).StatusCode);

        var large = await UploadAsync(_bea, insurance, new byte[MyLifeRules.MaxDocumentBytes + 1], "Gross.pdf");
        var refused = await _bea.PutAsJsonAsync($"/api/documents/{large}/mylife", new MyLifeFlagRequest(true), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains(MyLifeErrors.TooLarge, await refused.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlyTheMyLifeOriginMayAskFromTheBrowser()
    {
        using var client = Bridge(null);

        async Task<HttpResponseMessage> PreflightAsync(string path, string origin)
        {
            using var request = new HttpRequestMessage(HttpMethod.Options, path);
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Access-Control-Request-Method", "GET");
            request.Headers.Add("Access-Control-Request-Headers", "authorization");
            return await client.SendAsync(request, Ct);
        }

        var allowed = await PreflightAsync("/api/mylife/export", "https://mylife.example.test");
        Assert.Equal("https://mylife.example.test", allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.False(allowed.Headers.Contains("Access-Control-Allow-Credentials"));

        Assert.False((await PreflightAsync("/api/mylife/export", "https://evil.example.test")).Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False((await PreflightAsync("/api/objects", "https://mylife.example.test")).Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task ThirtyDaysOfChangesGiveATaskAndTheConfirmationClosesIt()
    {
        var insurance = await CreateAsync(_bea, "insurance", "Rechtsschutz", new() { ["insuranceKind"] = "legal", ["insurer"] = "ARAG" }, _beaPerson, _beaPerson);
        var token = await ConnectAsync(_bea);
        using var bridge = Bridge(token);
        Assert.Equal(HttpStatusCode.BadRequest, (await bridge.PostAsJsonAsync("/api/mylife/synced", new BridgeSyncedRequest("kein Fingerabdruck"), Ct)).StatusCode);
        var first = await ExportAsync(token);
        Assert.Equal(HttpStatusCode.NoContent, (await bridge.PostAsJsonAsync("/api/mylife/synced", new BridgeSyncedRequest(first.Fingerprint), Ct)).StatusCode);
        Assert.False((await _bea.GetFromJsonAsync<MyLifeLinkDto>("/api/account/mylife", Ct))!.Pending);

        // Eine Änderung ist offen, aber erst nach 30 Tagen ein Vorgang.
        var item = (await _bea.GetFromJsonAsync<ObjectDto>($"/api/objects/{insurance}", Ct))!;
        var data = item.Data.Clone();
        data.Fields["policyNumber"] = "R-1";
        Assert.Equal(HttpStatusCode.OK, (await _bea.PutAsJsonAsync($"/api/objects/{insurance}", new ObjectSaveRequest(item.TypeKey, data, item.OwnerPersonId, item.ResponsiblePersonId, item.Version), Ct)).StatusCode);
        Assert.True((await _bea.GetFromJsonAsync<MyLifeLinkDto>("/api/account/mylife", Ct))!.Pending);
        await RunReviewAsync();
        Assert.Null(await SyncTaskAsync());

        server.Clock.Advance(TimeSpan.FromDays(MyLifeRules.SyncDueDays + 1));
        await RunReviewAsync();
        var task = await SyncTaskAsync();
        Assert.NotNull(task);
        Assert.Equal(TaskStates.Open, task.State);
        await RunReviewAsync();
        Assert.Equal(1, await CountSyncTasksAsync());

        var current = await ExportAsync(token);
        Assert.NotEqual(first.Fingerprint, current.Fingerprint);
        Assert.Equal(HttpStatusCode.NoContent, (await bridge.PostAsJsonAsync("/api/mylife/synced", new BridgeSyncedRequest(current.Fingerprint), Ct)).StatusCode);
        Assert.Equal(TaskStates.Done, (await SyncTaskAsync())!.State);
    }

    private async Task RunReviewAsync()
    {
        await using var scope = server.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReviewEngine>().RunAsync(Ct);
    }

    private async Task<WorkTask?> SyncTaskAsync()
    {
        await using var scope = server.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MyDocsDbContext>().Tasks.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Kind == TaskKinds.MyLifeSync && t.ResponsiblePersonId == _beaPerson, Ct);
    }

    private async Task<int> CountSyncTasksAsync()
    {
        await using var scope = server.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MyDocsDbContext>().Tasks.CountAsync(t => t.Kind == TaskKinds.MyLifeSync && t.ResponsiblePersonId == _beaPerson, Ct);
    }
}
