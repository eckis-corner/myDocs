using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure;
using MyDocs.Server.Infrastructure.Audit;
using MyDocs.Shared.Audit;
using MyDocs.Shared.Auth;
using MyDocs.Shared.Documents;
using MyDocs.Shared.Household;
using MyDocs.Shared.Objects;

namespace MyDocs.Tests;

/// <summary>Audit-Log, Admin-Einsicht und Papierkorb (M1.7a).</summary>
public sealed class AuditTests(ServerFixture server) : IClassFixture<ServerFixture>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static int _counter;
    private HttpClient _admin = null!;
    private HttpClient _member = null!;
    private HttpClient _other = null!;
    private Guid _adminPerson;
    private Guid _memberPerson;
    private Guid _adminUser;
    private string _memberEmail = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var n = Interlocked.Increment(ref _counter);
        _admin = await TestAccounts.AdminAsync(server);
        _memberEmail = $"audit-bea{n}@example.test";
        _member = await TestAccounts.CreateAsync(server, _admin, _memberEmail, $"Bea {n}", HouseholdRoles.Member);
        _other = await TestAccounts.CreateAsync(server, _admin, $"audit-carl{n}@example.test", $"Carl {n}", HouseholdRoles.Member);
        var adminMe = await TestAccounts.MeAsync(_admin);
        (_adminPerson, _adminUser) = (adminMe.PersonId!.Value, adminMe.Id);
        _memberPerson = (await TestAccounts.MeAsync(_member)).PersonId!.Value;
    }

    public ValueTask DisposeAsync()
    {
        _admin.Dispose();
        _member.Dispose();
        _other.Dispose();
        return ValueTask.CompletedTask;
    }

    private static async Task<ObjectDto> CreateAsync(HttpClient client, string title, Guid? owner, Guid? responsible)
    {
        var data = new ObjectData { Title = title, Fields = new() { ["insuranceKind"] = "disability", ["insurer"] = "Alte Leipziger" } };
        var response = await client.PostAsJsonAsync("/api/objects", new ObjectSaveRequest("insurance", data, owner, responsible), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ObjectDto>(Ct))!;
    }

    private static Task<List<AuditEntryDto>?> MineAsync(HttpClient client) => client.GetFromJsonAsync<List<AuditEntryDto>>("/api/audit?take=1000", Ct);

    [Fact]
    public async Task LoginsAndFailedLoginsAreRecorded()
    {
        using var wrong = server.CreateApiClient();
        await wrong.PostAsJsonAsync("/api/auth/login", new LoginRequest(_memberEmail, "falsches Passwort!"), Ct);

        var log = (await MineAsync(_member))!;

        Assert.Contains(log, e => e.Action == AuditActions.Activated);
        Assert.Contains(log, e => e.Action == AuditActions.LoginFailed && e.Details == "Passwort");
        var all = (await _admin.GetFromJsonAsync<List<AuditEntryDto>>("/api/admin/audit?take=5000", Ct))!;
        Assert.Contains(all, e => e.Action == AuditActions.Login && e.ActorName == "Admin");
        Assert.Contains(all, e => e.Action == AuditActions.UserCreated && e.Details!.StartsWith(_memberEmail, StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.Forbidden, (await _member.GetAsync(new Uri("/api/admin/audit", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task OwnersSeeWhatOthersDoWithTheirPrivateEntries()
    {
        var mine = await CreateAsync(_member, "BU Bea", _memberPerson, null);
        var otherUser = (await TestAccounts.MeAsync(_other)).Id;
        await _member.PutAsJsonAsync($"/api/objects/{mine.Id}/shares", new List<ObjectShareDto> { new(otherUser, false) }, Ct);

        await _other.GetFromJsonAsync<ObjectDto>($"/api/objects/{mine.Id}", Ct);

        var log = (await MineAsync(_member))!;
        Assert.Contains(log, e => e.Action == AuditActions.ObjectCreated && e.ObjectId == mine.Id);
        Assert.Contains(log, e => e.Action == AuditActions.ObjectShared && e.ObjectId == mine.Id && e.Details!.EndsWith(": lesen", StringComparison.Ordinal));
        Assert.Contains(log, e => e.Action == AuditActions.ObjectViewed && e.ObjectId == mine.Id && e.ActorName!.StartsWith("Carl", StringComparison.Ordinal));

        // Wer keinen Bezug hat, sieht davon nichts.
        Assert.DoesNotContain((await MineAsync(_admin))!, e => e.ObjectId == mine.Id);
    }

    [Fact]
    public async Task TheAdminViewShowsPrivateEntriesReadOnlyAndTellsTheOwner()
    {
        var mine = await CreateAsync(_member, "Privates Konto", _memberPerson, null);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync(new Uri($"/api/objects/{mine.Id}", UriKind.Relative), Ct)).StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/api/objects/{mine.Id}", UriKind.Relative));
        request.Headers.Add(AdminViewHeader.Name, AdminViewHeader.Value);
        var seen = await _admin.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, seen.StatusCode);
        var item = (await seen.Content.ReadFromJsonAsync<ObjectDto>(Ct))!;
        Assert.Equal((false, false), (item.CanEdit, item.CanManage));

        using var change = new HttpRequestMessage(HttpMethod.Put, new Uri($"/api/objects/{mine.Id}", UriKind.Relative))
        {
            Content = JsonContent.Create(new ObjectSaveRequest("insurance", item.Data, _memberPerson, null, item.Version)),
        };
        change.Headers.Add(AdminViewHeader.Name, AdminViewHeader.Value);
        Assert.Equal(HttpStatusCode.Forbidden, (await _admin.SendAsync(change, Ct)).StatusCode);

        var log = (await MineAsync(_member))!;
        Assert.Contains(log, e => e.Action == AuditActions.AdminView && e.ObjectId == mine.Id && e.ActorName == "Admin");
    }

    [Fact]
    public async Task OnlyAdministratorsCanUseTheAdminView()
    {
        var mine = await CreateAsync(_member, "Geheim", _memberPerson, null);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/api/objects/{mine.Id}", UriKind.Relative));
        request.Headers.Add(AdminViewHeader.Name, AdminViewHeader.Value);

        Assert.Equal(HttpStatusCode.NotFound, (await _other.SendAsync(request, Ct)).StatusCode);
    }

    [Fact]
    public async Task TheTrashIsEmptiedAfterThirtyDaysWithItsFiles()
    {
        var item = await CreateAsync(_admin, "Alte Police", null, _adminPerson);
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("nur hier " + Guid.NewGuid()));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "alt.pdf");
        var document = (await (await _admin.PostAsync(new Uri($"/api/objects/{item.Id}/documents", UriKind.Relative), form, Ct)).Content.ReadFromJsonAsync<DocumentDto>(Ct))!;
        string hash;
        using (var scope = server.Services.CreateScope())
        {
            hash = await scope.ServiceProvider.GetRequiredService<MyDocsDbContext>().DocumentVersions.Where(v => v.DocumentId == document.Id).Select(v => v.BlobHash).SingleAsync(Ct);
        }

        var path = Path.Combine(server.DataDirectory, "files", hash[..2], hash);
        Assert.True(File.Exists(path));
        await _admin.DeleteAsync(new Uri($"/api/objects/{item.Id}", UriKind.Relative), Ct);

        var purge = server.Services.GetRequiredService<TrashPurge>();
        await purge.PurgeAsync(Ct);
        Assert.Contains(await _admin.GetFromJsonAsync<List<ObjectSummaryDto>>("/api/objects/trash", Ct) ?? [], o => o.Id == item.Id);

        server.Clock.Advance(TimeSpan.FromDays(31));
        var (objects, documents) = await purge.PurgeAsync(Ct);

        // Nach 31 Tagen ist auch die Sitzung abgelaufen; deshalb prüft der Test direkt in der Datenbank.
        Assert.True(objects >= 1 && documents >= 1);
        using var check = server.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<MyDocsDbContext>();
        Assert.False(await db.Objects.AnyAsync(o => o.Id == item.Id, Ct));
        Assert.False(await db.Documents.AnyAsync(d => d.Id == document.Id, Ct));
        Assert.False(await db.Blobs.AnyAsync(b => b.Hash == hash, Ct));
        Assert.False(File.Exists(path));
        Assert.True(await db.AuditLog.AnyAsync(a => a.Action == AuditActions.TrashPurged, Ct));
        // Das Protokoll bleibt: Der Eintrag zum Anlegen der Police steht noch darin.
        Assert.True(await db.AuditLog.AnyAsync(a => a.Action == AuditActions.ObjectCreated && a.ObjectId == item.Id, Ct));
    }

    [Theory]
    [InlineData("192.168.17.42", "192.168.17.0")]
    [InlineData("::ffff:10.1.2.3", "10.1.2.0")]
    [InlineData("2001:db8:85a3:8d3:1319:8a2e:370:7348", "2001:db8:85a3::")]
    public void IpAddressesAreShortened(string address, string expected) =>
        Assert.Equal(expected, AuditLog.Truncate(System.Net.IPAddress.Parse(address)));
}
