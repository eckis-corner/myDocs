using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure;
using MyDocs.Server.Infrastructure.Tasks;
using MyDocs.Shared.Audit;
using MyDocs.Shared.Dashboard;
using MyDocs.Shared.Documents;
using MyDocs.Shared.Household;
using MyDocs.Shared.Objects;
using MyDocs.Shared.Search;
using MyDocs.Shared.Tasks;

namespace MyDocs.Tests;

/// <summary>Posteingang (M3.3): privat und für den Haushalt, zuordnen, weitergeben, verwerfen, fällig nach 7 Tagen.</summary>
public sealed class InboxTests(ServerFixture server) : IClassFixture<ServerFixture>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static int _counter;
    private HttpClient _admin = null!;
    private HttpClient _bea = null!;
    private HttpClient _carl = null!;
    private HttpClient _guest = null!;
    private Guid _beaPerson;
    private Guid _carlPerson;

    public async ValueTask InitializeAsync()
    {
        var n = Interlocked.Increment(ref _counter);
        _admin = await TestAccounts.AdminAsync(server);
        _bea = await TestAccounts.CreateAsync(server, _admin, $"inbox-bea{n}@example.test", $"Bea {n}", HouseholdRoles.Member);
        _carl = await TestAccounts.CreateAsync(server, _admin, $"inbox-carl{n}@example.test", $"Carl {n}", HouseholdRoles.Member);
        _guest = await TestAccounts.CreateAsync(server, _admin, $"inbox-opa{n}@example.test", $"Opa {n}", HouseholdRoles.Guest);
        _beaPerson = (await TestAccounts.MeAsync(_bea)).PersonId!.Value;
        _carlPerson = (await TestAccounts.MeAsync(_carl)).PersonId!.Value;
    }

    public ValueTask DisposeAsync()
    {
        _admin.Dispose();
        _bea.Dispose();
        _carl.Dispose();
        _guest.Dispose();
        return ValueTask.CompletedTask;
    }

    private static async Task<InboxItemDto> UploadAsync(HttpClient client, string content, string fileName = "Scan_0001.pdf", bool household = false)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", fileName);
        var response = await client.PostAsync(new Uri($"/api/inbox{(household ? "?household=true" : string.Empty)}", UriKind.Relative), form, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InboxItemDto>(Ct))!;
    }

    private static async Task<Guid> ObjectAsync(HttpClient client, Guid person, bool isPrivate, string title = "Hausrat")
    {
        var data = new ObjectData { Title = title, Fields = new() { ["insuranceKind"] = "liability", ["insurer"] = "HUK" } };
        var response = await client.PostAsJsonAsync("/api/objects", new ObjectSaveRequest("insurance", data, isPrivate ? person : null, person), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ObjectDto>(Ct))!.Id;
    }

    private static async Task<List<InboxItemDto>> ListAsync(HttpClient client, bool discarded = false) =>
        (await client.GetFromJsonAsync<List<InboxItemDto>>($"/api/inbox{(discarded ? "?discarded=true" : string.Empty)}", Ct))!;

    private static Task<HttpResponseMessage> AssignAsync(HttpClient client, Guid id, InboxAssignRequest request) =>
        client.PostAsJsonAsync($"/api/inbox/{id}/assign", request, Ct);

    [Fact]
    public async Task APrivateInboxIsSeenOnlyByItsPerson()
    {
        var item = await UploadAsync(_bea, "%PDF-1.4 Brief der Krankenkasse");

        Assert.False(item.Household);
        Assert.Equal(DocumentSources.Upload, item.Source);
        Assert.Equal("Scan 0001", item.Title);
        Assert.Contains(await ListAsync(_bea), i => i.Id == item.Id);
        Assert.DoesNotContain(await ListAsync(_carl), i => i.Id == item.Id);
        Assert.DoesNotContain(await ListAsync(_admin), i => i.Id == item.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await _carl.GetAsync($"/api/documents/{item.Id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _carl.GetAsync($"/api/documents/{item.Id}/content", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _bea.GetAsync($"/api/documents/{item.Id}/content", Ct)).StatusCode);
        // Auch die Admin-Einsicht öffnet keinen fremden Posteingang.
        using var adminView = new HttpRequestMessage(HttpMethod.Get, $"/api/documents/{item.Id}");
        adminView.Headers.Add(AdminViewHeader.Name, AdminViewHeader.Value);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.SendAsync(adminView, Ct)).StatusCode);
    }

    [Fact]
    public async Task TheHouseholdInboxIsForMembersNotForGuests()
    {
        var item = await UploadAsync(_bea, "%PDF-1.4 Rundschreiben der Hausverwaltung", household: true);

        Assert.True(item.Household);
        Assert.Contains(await ListAsync(_carl), i => i.Id == item.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await _guest.GetAsync("/api/inbox", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _guest.GetAsync($"/api/documents/{item.Id}", Ct)).StatusCode);
    }

    [Fact]
    public async Task AssigningSetsTheDetailsLinksTheEntriesAndCanCreateATask()
    {
        var item = await UploadAsync(_bea, "%PDF-1.4 Beitragsrechnung 2027");
        var first = await ObjectAsync(_bea, _beaPerson, isPrivate: false, "Hausrat");
        var second = await ObjectAsync(_bea, _beaPerson, isPrivate: true, "Glas");

        Assert.Equal(HttpStatusCode.BadRequest, (await AssignAsync(_bea, item.Id, new InboxAssignRequest("Rechnung", "invoice", null, [], null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AssignAsync(_bea, item.Id, new InboxAssignRequest(" ", "invoice", null, [first], null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AssignAsync(_bea, item.Id, new InboxAssignRequest("Rechnung", "pizza", null, [first], null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AssignAsync(_bea, item.Id, new InboxAssignRequest("Rechnung", "invoice", null, [first], " ", null))).StatusCode);

        var response = await AssignAsync(_bea, item.Id, new InboxAssignRequest("Beitragsrechnung 2027", "invoice", new DateOnly(2027, 1, 2), [first, second], "Beitrag überweisen", new DateOnly(2027, 1, 15)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = (await response.Content.ReadFromJsonAsync<DocumentDto>(Ct))!;
        Assert.Equal(("Beitragsrechnung 2027", "invoice", new DateOnly(2027, 1, 2)), (document.Title, document.Kind, document.DocumentDate));
        Assert.Equal(new[] { first, second }.Order(), document.ObjectIds.Order());

        Assert.DoesNotContain(await ListAsync(_bea), i => i.Id == item.Id);
        var documents = (await _bea.GetFromJsonAsync<List<DocumentDto>>($"/api/objects/{first}/documents", Ct))!;
        Assert.Contains(documents, d => d.Id == item.Id && d.Kind == "invoice");
        // Carl sieht das Dokument über den Haushaltseintrag.
        Assert.Equal(HttpStatusCode.OK, (await _carl.GetAsync($"/api/documents/{item.Id}", Ct)).StatusCode);

        var tasks = (await _bea.GetFromJsonAsync<List<TaskDto>>("/api/tasks?scope=mine", Ct))!;
        var task = Assert.Single(tasks, t => t.Title == "Beitrag überweisen");
        Assert.Equal((TaskKinds.Task, _beaPerson, new DateOnly(2027, 1, 15)), (task.Kind, task.ResponsiblePersonId!.Value, task.DueOn!.Value));

        var audit = (await _bea.GetFromJsonAsync<List<AuditEntryDto>>("/api/audit", Ct))!;
        Assert.Contains(audit, a => a.Action == AuditActions.DocumentAssigned && a.DocumentId == item.Id);
    }

    [Fact]
    public async Task OnlyEditableEntriesCanBeAssigned()
    {
        var item = await UploadAsync(_bea, "%PDF-1.4 Fremder Brief");
        var carlsPrivate = await ObjectAsync(_carl, _carlPerson, isPrivate: true, "Carls Konto");

        Assert.Equal(HttpStatusCode.Forbidden, (await AssignAsync(_bea, item.Id, new InboxAssignRequest("Brief", null, null, [carlsPrivate], null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AssignAsync(_bea, item.Id, new InboxAssignRequest("Brief", null, null, [Guid.NewGuid()], null, null))).StatusCode);
        // Fremde Dokumente im Posteingang lassen sich nicht zuordnen.
        var mine = await ObjectAsync(_carl, _carlPerson, isPrivate: false, "Carls Hausrat");
        Assert.Equal(HttpStatusCode.NotFound, (await AssignAsync(_carl, item.Id, new InboxAssignRequest("Brief", null, null, [mine], null, null))).StatusCode);
    }

    [Fact]
    public async Task InboxItemsMoveBetweenPrivateAndHousehold()
    {
        var item = await UploadAsync(_bea, "%PDF-1.4 Wer kümmert sich?");

        var shared = await _bea.PutAsJsonAsync($"/api/inbox/{item.Id}/owner", new InboxOwnerRequest(true), Ct);
        Assert.True((await shared.Content.ReadFromJsonAsync<InboxItemDto>(Ct))!.Household);
        Assert.Contains(await ListAsync(_carl), i => i.Id == item.Id);

        var taken = await _carl.PutAsJsonAsync($"/api/inbox/{item.Id}/owner", new InboxOwnerRequest(false), Ct);
        Assert.False((await taken.Content.ReadFromJsonAsync<InboxItemDto>(Ct))!.Household);
        Assert.DoesNotContain(await ListAsync(_bea), i => i.Id == item.Id);
        Assert.Contains(await ListAsync(_carl), i => i.Id == item.Id);
    }

    [Fact]
    public async Task DiscardedItemsCanBeRestoredAndAreKeptUntilTheTrashIsEmptied()
    {
        var kept = await UploadAsync(_bea, "%PDF-1.4 bleibt liegen");
        var discarded = await UploadAsync(_bea, "%PDF-1.4 Werbung");

        Assert.Equal(HttpStatusCode.NoContent, (await _bea.DeleteAsync($"/api/documents/{discarded.Id}", Ct)).StatusCode);
        Assert.DoesNotContain(await ListAsync(_bea), i => i.Id == discarded.Id);
        Assert.Contains(await ListAsync(_bea, discarded: true), i => i.Id == discarded.Id);
        Assert.Equal(HttpStatusCode.OK, (await _bea.PostAsync(new Uri($"/api/documents/{discarded.Id}/restore", UriKind.Relative), null, Ct)).StatusCode);
        Assert.Contains(await ListAsync(_bea), i => i.Id == discarded.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await _bea.DeleteAsync($"/api/documents/{discarded.Id}", Ct)).StatusCode);

        // Dokumente ohne Eintrag verschwinden beim Leeren, außer sie liegen im Posteingang.
        var purge = server.Services.GetRequiredService<TrashPurge>();
        await purge.PurgeAsync(Ct);
        await using (var scope = server.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyDocsDbContext>();
            Assert.True(await db.Documents.AnyAsync(d => d.Id == kept.Id, Ct));
            Assert.True(await db.Documents.AnyAsync(d => d.Id == discarded.Id, Ct));
        }

        server.Clock.Advance(TrashPurge.RetentionPeriod + TimeSpan.FromDays(1));
        await purge.PurgeAsync(Ct);
        await using (var scope = server.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyDocsDbContext>();
            Assert.True(await db.Documents.AnyAsync(d => d.Id == kept.Id, Ct));
            Assert.False(await db.Documents.AnyAsync(d => d.Id == discarded.Id, Ct));
        }
    }

    [Fact]
    public async Task AnInboxIsDueAfterSevenDaysAndTheTaskClosesWhenItIsEmpty()
    {
        var item = await UploadAsync(_bea, "%PDF-1.4 liegt zu lange");
        var rule = $"inbox:{_beaPerson}";

        await RunReviewAsync();
        Assert.False(await HasOpenTaskAsync(rule));

        // Das Dokument kam vor 8 Tagen (statt die Uhr vorzustellen und alle Sitzungen zu beenden).
        await using (var scope = server.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyDocsDbContext>();
            var document = await db.Documents.FirstAsync(d => d.Id == item.Id, Ct);
            document.CreatedAt = server.Clock.GetUtcNow().AddDays(-(ReviewRules.InboxDueDays + 1));
            await db.SaveChangesAsync(Ct);
        }

        await RunReviewAsync();
        await RunReviewAsync();
        Assert.True(await HasOpenTaskAsync(rule));

        // Nur Bea sieht den Vorgang zu ihrem privaten Posteingang, auch nicht über den Haushalt.
        Assert.Contains((await _bea.GetFromJsonAsync<List<TaskDto>>("/api/tasks", Ct))!, t => t.Kind == TaskKinds.Inbox && t.ResponsiblePersonId == _beaPerson);
        Assert.DoesNotContain((await _carl.GetFromJsonAsync<List<TaskDto>>("/api/tasks", Ct))!, t => t.Kind == TaskKinds.Inbox && t.ResponsiblePersonId == _beaPerson);

        var target = await ObjectAsync(_bea, _beaPerson, isPrivate: false);
        Assert.Equal(HttpStatusCode.OK, (await AssignAsync(_bea, item.Id, new InboxAssignRequest("Brief", "letter", null, [target], null, null))).StatusCode);
        await RunReviewAsync();
        Assert.False(await HasOpenTaskAsync(rule));
    }

    [Fact]
    public async Task TheDashboardCountsTheInboxAndTheSearchFindsIt()
    {
        await UploadAsync(_carl, "%PDF-1.4 Zählerstand Strom Haushalt", "Strom.pdf", household: true);
        var mine = await UploadAsync(_carl, "%PDF-1.4 Steuerbescheid Zinsabschlag XZ-55", "Bescheid.pdf");

        var myDashboard = (await _carl.GetFromJsonAsync<DashboardDto>("/api/dashboard?scope=mine", Ct))!;
        Assert.Contains(myDashboard.InboxOldest, i => i.Id == mine.Id);
        Assert.DoesNotContain(myDashboard.InboxOldest, i => i.Household);
        var household = (await _carl.GetFromJsonAsync<DashboardDto>("/api/dashboard?scope=household", Ct))!;
        Assert.True(household.InboxCount > myDashboard.InboxCount);

        var hits = (await _carl.GetFromJsonAsync<List<SearchHitDto>>("/api/search?q=Bescheid", Ct))!;
        var hit = Assert.Single(hits, h => h.Id == mine.Id);
        Assert.Null(hit.ObjectId);
        Assert.DoesNotContain((await _bea.GetFromJsonAsync<List<SearchHitDto>>("/api/search?q=Bescheid", Ct))!, h => h.Id == mine.Id);
    }

    private static async Task<HttpResponseMessage> CameraAsync(HttpClient client, params byte[][] pages)
    {
        using var form = new MultipartFormDataContent();
        for (var i = 0; i < pages.Length; i++)
        {
            var page = new ByteArrayContent(pages[i]);
            page.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(page, "page", $"seite-{i + 1}.jpg");
        }

        return await client.PostAsync(new Uri("/api/inbox/camera", UriKind.Relative), form, Ct);
    }

    [Fact]
    public async Task CameraPagesBecomeOnePdfInThePrivateInbox()
    {
        var response = await CameraAsync(_bea, TestImages.RgbJpeg, TestImages.GrayJpeg);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = (await response.Content.ReadFromJsonAsync<InboxItemDto>(Ct))!;

        Assert.Equal((DocumentSources.Camera, false), (item.Source, item.Household));
        Assert.StartsWith("Kamera ", item.Title, StringComparison.Ordinal);
        Assert.Equal("application/pdf", item.Current.ContentType);
        var pdf = Encoding.Latin1.GetString(await _bea.GetByteArrayAsync($"/api/documents/{item.Id}/content", Ct));
        Assert.StartsWith("%PDF-1.4", pdf, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Count(pdf, "/Type /Page /"));
        Assert.Contains("/DeviceGray", pdf, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain(await ListAsync(_carl), i => i.Id == item.Id);
    }

    [Fact]
    public async Task CameraAcceptsOnlyJpegsAndALimitedNumberOfPages()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await CameraAsync(_bea, FakeWorker.PngSignature)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CameraAsync(_bea)).StatusCode);
        var tooMany = Enumerable.Repeat(TestImages.GrayJpeg, CameraLimits.MaxPages + 1).ToArray();
        var response = await CameraAsync(_bea, tooMany);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(DocumentErrors.TooManyPages, await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Forbidden, (await CameraAsync(_guest, TestImages.RgbJpeg)).StatusCode);
    }

    [Fact]
    public void JpegSizesAndColorsAreRead()
    {
        Assert.Equal(new MyDocs.Server.Infrastructure.Inbox.JpegPdf.JpegInfo(64, 48, 3), MyDocs.Server.Infrastructure.Inbox.JpegPdf.Read(TestImages.RgbJpeg));
        Assert.Equal(new MyDocs.Server.Infrastructure.Inbox.JpegPdf.JpegInfo(40, 30, 1), MyDocs.Server.Infrastructure.Inbox.JpegPdf.Read(TestImages.GrayJpeg));
        Assert.Throws<MyDocs.Server.Infrastructure.Inbox.InvalidJpegException>(() => MyDocs.Server.Infrastructure.Inbox.JpegPdf.Read("GIF89a"u8));
    }

    private async Task RunReviewAsync()
    {
        await using var scope = server.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReviewEngine>().RunAsync(Ct);
    }

    private async Task<bool> HasOpenTaskAsync(string rule)
    {
        await using var scope = server.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyDocsDbContext>();
        return await db.Tasks.CountAsync(t => t.RuleKey == rule && t.State != TaskStates.Done && t.State != TaskStates.Discarded, Ct) switch
        {
            0 => false,
            1 => true,
            var n => throw new InvalidOperationException($"{n} offene Vorgänge für {rule}"),
        };
    }
}
