using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Search;
using MyDocs.Shared.Documents;
using MyDocs.Shared.Household;
using MyDocs.Shared.Objects;
using MyDocs.Shared.Search;

namespace MyDocs.Tests;

/// <summary>Suche (M1.6): Wortstämme, Auswahlwerte, Teilnummern, Tippfehler, Dokumente und Rechte.</summary>
public sealed class SearchTests(ServerFixture server) : IClassFixture<ServerFixture>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private HttpClient _admin = null!;
    private HttpClient _member = null!;
    private Guid _adminPerson;
    private Guid _memberPerson;
    private static int _counter;

    public async ValueTask InitializeAsync()
    {
        var n = Interlocked.Increment(ref _counter);
        _admin = await TestAccounts.AdminAsync(server);
        _member = await TestAccounts.CreateAsync(server, _admin, $"search{n}@example.test", $"Suse {n}", HouseholdRoles.Member);
        _adminPerson = (await TestAccounts.MeAsync(_admin)).PersonId!.Value;
        _memberPerson = (await TestAccounts.MeAsync(_member)).PersonId!.Value;
    }

    public ValueTask DisposeAsync()
    {
        _admin.Dispose();
        _member.Dispose();
        return ValueTask.CompletedTask;
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string type, string title, Dictionary<string, string> fields, Guid? owner, Guid? responsible, string? notes = null)
    {
        var response = await client.PostAsJsonAsync("/api/objects", new ObjectSaveRequest(type, new ObjectData { Title = title, Fields = fields, Notes = notes }, owner, responsible), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ObjectDto>(Ct))!.Id;
    }

    private static async Task<List<SearchHitDto>> SearchAsync(HttpClient client, string query) =>
        (await client.GetFromJsonAsync<List<SearchHitDto>>($"/api/search?q={Uri.EscapeDataString(query)}", Ct))!;

    [Fact]
    public async Task FindsByWordStemChoiceLabelsAndNotes()
    {
        var id = await CreateAsync(_admin, "insurance", "Versicherungen der Familie Sommer", new() { ["insuranceKind"] = "liability", ["insurer"] = "Gothaer" },
            null, _adminPerson, notes: "Beitrag steigt ab Januar");

        Assert.Contains(await SearchAsync(_member, "Versicherung Sommer"), h => h.Id == id);
        Assert.Contains(await SearchAsync(_member, "Privathaftpflicht"), h => h.Id == id);
        Assert.Contains(await SearchAsync(_member, "liability"), h => h.Id == id);
        var hit = (await SearchAsync(_member, "Januar")).Single(h => h.Id == id);
        Assert.Equal(("Beitrag steigt ab Januar", "insurance", SearchKinds.Entry), (hit.Snippet, hit.TypeKey, hit.Kind));
    }

    [Fact]
    public async Task FindsPartsOfNumbersAndTypos()
    {
        var id = await CreateAsync(_admin, "bankAccount", "Girokonto Haushalt", new() { ["bank"] = "Allianz Bank", ["iban"] = "DE89370400440532013000" }, null, _adminPerson);

        Assert.Contains(await SearchAsync(_member, "05320130"), h => h.Id == id);
        Assert.Contains(await SearchAsync(_member, "0532 0130"), h => h.Id == id);
        Assert.Contains(await SearchAsync(_member, "Alianz"), h => h.Id == id);
    }

    [Fact]
    public async Task FindsDocumentsByNameAndLeadsToTheirObject()
    {
        var objectId = await CreateAsync(_admin, "contract", "Stromvertrag", new() { ["contractKind"] = "electricity", ["provider"] = "Stadtwerke" }, null, _adminPerson);
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "Jahresabrechnung_Strom_2026.pdf");
        var upload = await _admin.PostAsync(new Uri($"/api/objects/{objectId}/documents", UriKind.Relative), form, Ct);
        var document = (await upload.Content.ReadFromJsonAsync<DocumentDto>(Ct))!;

        var hit = (await SearchAsync(_member, "Jahresabrechnung")).Single(h => h.Id == document.Id);

        Assert.Equal((SearchKinds.Document, objectId, "Stromvertrag"), (hit.Kind, hit.ObjectId, hit.ObjectTitle));
    }

    [Fact]
    public async Task PrivateEntriesOfOthersAreNeverFound()
    {
        var marker = "Geheimwort" + Guid.NewGuid().ToString("N")[..6];
        var id = await CreateAsync(_member, "insurance", $"BU {marker}", new() { ["insuranceKind"] = "disability", ["insurer"] = "Alte Leipziger" }, _memberPerson, null);

        Assert.Contains(await SearchAsync(_member, marker), h => h.Id == id);
        Assert.Empty(await SearchAsync(_admin, marker));
    }

    [Fact]
    public async Task MissingSearchTextsAreFilledAtStartup()
    {
        var id = await CreateAsync(_admin, "vehicle", "Familienauto", new() { ["vehicleKind"] = "car", ["makeModel"] = "Kombi Zuverlässig" }, null, _adminPerson);
        using (var scope = server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MyDocsDbContext>();
            await db.Objects.Where(o => o.Id == id).ExecuteUpdateAsync(s => s.SetProperty(o => o.SearchText, (string?)null), Ct);
        }

        Assert.Empty(await SearchAsync(_admin, "Zuverlässig"));
        await SearchIndex.BackfillAsync(server.Services, Ct);

        Assert.Contains(await SearchAsync(_admin, "Zuverlässig"), h => h.Id == id);
    }

    [Fact]
    public async Task ShortOrEmptyQueriesReturnNothing()
    {
        Assert.Empty(await SearchAsync(_admin, "a"));
        Assert.Empty(await SearchAsync(_admin, "   "));
        Assert.Equal(HttpStatusCode.OK, (await _admin.GetAsync(new Uri("/api/search?q=%25%25_", UriKind.Relative), Ct)).StatusCode);
    }
}
