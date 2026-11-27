using System.Net;
using System.Net.Http.Json;
using MyDocs.Shared.Household;
using MyDocs.Shared.Objects;

namespace MyDocs.Tests;

/// <summary>Objekte, Rechte, Freigaben, Konflikte, Historie und Papierkorb (M1.4).</summary>
public sealed class ObjectTests(ServerFixture server) : IClassFixture<ServerFixture>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private HttpClient _admin = null!;
    private HttpClient _member = null!;
    private HttpClient _guest = null!;
    private Guid _adminPerson;
    private Guid _memberPerson;
    private static int _counter;

    public async ValueTask InitializeAsync()
    {
        var n = Interlocked.Increment(ref _counter);
        _admin = await TestAccounts.AdminAsync(server);
        _member = await TestAccounts.CreateAsync(server, _admin, $"bea{n}@example.test", $"Bea {n}", HouseholdRoles.Member);
        _guest = await TestAccounts.CreateAsync(server, _admin, $"opa{n}@example.test", $"Opa {n}", HouseholdRoles.Guest);
        _adminPerson = (await TestAccounts.MeAsync(_admin)).PersonId!.Value;
        _memberPerson = (await TestAccounts.MeAsync(_member)).PersonId!.Value;
    }

    public ValueTask DisposeAsync()
    {
        _admin.Dispose();
        _member.Dispose();
        _guest.Dispose();
        return ValueTask.CompletedTask;
    }

    private static ObjectData Insurance(string title = "Haftpflicht") => new()
    {
        Title = title,
        Fields = new() { ["insuranceKind"] = "liability", ["insurer"] = "HUK", ["premium"] = "65.5", ["frequency"] = "yearly" },
    };

    private static async Task<ObjectDto> CreateAsync(HttpClient client, ObjectSaveRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/objects", request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ObjectDto>(Ct))!;
    }

    [Fact]
    public async Task HouseholdObjectsAreReadByAllAndChangedByMembers()
    {
        var created = await CreateAsync(_admin, new ObjectSaveRequest("insurance", Insurance(), null, _adminPerson));

        var seen = (await _guest.GetFromJsonAsync<ObjectDto>($"/api/objects/{created.Id}", Ct))!;
        Assert.Equal(("HUK", false), (seen.Data.Get("insurer"), seen.CanEdit));
        Assert.Contains(await _guest.GetFromJsonAsync<List<ObjectSummaryDto>>("/api/objects?area=4", Ct) ?? [], o => o.Id == created.Id);

        var guestEdit = await _guest.PutAsJsonAsync($"/api/objects/{created.Id}", new ObjectSaveRequest("insurance", Insurance("Gast"), null, _adminPerson, created.Version), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, guestEdit.StatusCode);
        var guestCreate = await _guest.PostAsJsonAsync("/api/objects", new ObjectSaveRequest("insurance", Insurance(), null, _adminPerson), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, guestCreate.StatusCode);

        var memberEdit = await _member.PutAsJsonAsync($"/api/objects/{created.Id}", new ObjectSaveRequest("insurance", Insurance("Privathaftpflicht"), null, _memberPerson, created.Version), Ct);
        Assert.Equal(HttpStatusCode.OK, memberEdit.StatusCode);
    }

    [Fact]
    public async Task InvalidObjectsAreRejectedWithTheirFields()
    {
        var data = Insurance();
        data.Fields.Remove("insurer");
        data.Fields["premium"] = "viel";

        var response = await _admin.PostAsJsonAsync("/api/objects", new ObjectSaveRequest("insurance", data, null, null), Ct);

        var problem = (await response.Content.ReadFromJsonAsync<ObjectValidationProblem>(Ct))!;
        Assert.Equal(ObjectErrors.Invalid, problem.Title);
        Assert.Equal(("required", "number", "required"), (problem.Errors["insurer"], problem.Errors["premium"], problem.Errors["responsible"]));

        var mylifeOnly = await _admin.PostAsJsonAsync("/api/objects", new ObjectSaveRequest("funeral", new ObjectData { Title = "Bestattung" }, null, _adminPerson), Ct);
        Assert.Equal(ObjectErrors.TypeOnlyInMyLife, await SetupTests.TitleAsync(mylifeOnly));
    }

    [Fact]
    public async Task PrivateObjectsStayPrivateUntilShared()
    {
        var mine = await CreateAsync(_member, new ObjectSaveRequest("insurance", Insurance("BU"), _memberPerson, null));

        // Andere sehen ihn nicht, auch nicht in Listen.
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync(new Uri($"/api/objects/{mine.Id}", UriKind.Relative), Ct)).StatusCode);
        Assert.DoesNotContain(await _admin.GetFromJsonAsync<List<ObjectSummaryDto>>("/api/objects", Ct) ?? [], o => o.Id == mine.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync(new Uri($"/api/objects/{mine.Id}/history", UriKind.Relative), Ct)).StatusCode);

        // Privat anlegen geht nur für sich selbst.
        var forOthers = await _admin.PostAsJsonAsync("/api/objects", new ObjectSaveRequest("insurance", Insurance(), _memberPerson, null), Ct);
        Assert.Equal(ObjectErrors.OwnerInvalid, await SetupTests.TitleAsync(forOthers));

        var adminUser = (await TestAccounts.MeAsync(_admin)).Id;
        await _member.PutAsJsonAsync($"/api/objects/{mine.Id}/shares", new List<ObjectShareDto> { new(adminUser, false) }, Ct);
        var shared = (await _admin.GetFromJsonAsync<ObjectDto>($"/api/objects/{mine.Id}", Ct))!;
        Assert.Equal((false, false), (shared.CanEdit, shared.CanManage));
        Assert.Empty(shared.Shares);

        await _member.PutAsJsonAsync($"/api/objects/{mine.Id}/shares", new List<ObjectShareDto> { new(adminUser, true) }, Ct);
        var edited = await _admin.PutAsJsonAsync($"/api/objects/{mine.Id}", new ObjectSaveRequest("insurance", Insurance("BU geändert"), _memberPerson, null, shared.Version), Ct);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);

        // Wer nur eine Freigabe hat, nimmt das Objekt nicht in den Haushalt und löscht es nicht.
        var current = (await edited.Content.ReadFromJsonAsync<ObjectDto>(Ct))!;
        var takeOver = await _admin.PutAsJsonAsync($"/api/objects/{mine.Id}", new ObjectSaveRequest("insurance", current.Data, null, _adminPerson, current.Version), Ct);
        Assert.Equal(ObjectErrors.OwnerInvalid, await SetupTests.TitleAsync(takeOver));
        Assert.Equal(HttpStatusCode.Forbidden, (await _admin.DeleteAsync(new Uri($"/api/objects/{mine.Id}", UriKind.Relative), Ct)).StatusCode);

        // Die Eigentümerin gibt das Objekt in den Haushalt; damit entfallen die Freigaben.
        var toHousehold = await _member.PutAsJsonAsync($"/api/objects/{mine.Id}", new ObjectSaveRequest("insurance", current.Data, null, _memberPerson, current.Version), Ct);
        Assert.Equal(HttpStatusCode.OK, toHousehold.StatusCode);
        Assert.True((await _guest.GetFromJsonAsync<ObjectDto>($"/api/objects/{mine.Id}", Ct))!.OwnerPersonId is null);
    }

    [Fact]
    public async Task OnlyTheResponsiblePersonMakesAHouseholdObjectPrivate()
    {
        var item = await CreateAsync(_admin, new ObjectSaveRequest("contract", new ObjectData { Title = "Strom", Fields = new() { ["contractKind"] = "electricity", ["provider"] = "Stadtwerke" } }, null, _memberPerson));

        var byOther = await _admin.PutAsJsonAsync($"/api/objects/{item.Id}", new ObjectSaveRequest("contract", item.Data, _adminPerson, _memberPerson, item.Version), Ct);
        Assert.Equal(ObjectErrors.OwnerInvalid, await SetupTests.TitleAsync(byOther));

        var byResponsible = await _member.PutAsJsonAsync($"/api/objects/{item.Id}", new ObjectSaveRequest("contract", item.Data, _memberPerson, _memberPerson, item.Version), Ct);
        Assert.Equal(HttpStatusCode.OK, byResponsible.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync(new Uri($"/api/objects/{item.Id}", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task SimultaneousChangesAreDetected()
    {
        var item = await CreateAsync(_admin, new ObjectSaveRequest("insurance", Insurance(), null, _adminPerson));

        var first = await _admin.PutAsJsonAsync($"/api/objects/{item.Id}", new ObjectSaveRequest("insurance", Insurance("Erste"), null, _adminPerson, item.Version), Ct);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var second = await _member.PutAsJsonAsync($"/api/objects/{item.Id}", new ObjectSaveRequest("insurance", Insurance("Zweite"), null, _adminPerson, item.Version), Ct);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("Erste", (await _guest.GetFromJsonAsync<ObjectDto>($"/api/objects/{item.Id}", Ct))!.Data.Title);
    }

    [Fact]
    public async Task ChangesAreRecordedInTheHistory()
    {
        var item = await CreateAsync(_admin, new ObjectSaveRequest("insurance", Insurance(), null, _adminPerson));
        var changed = Insurance("Haftpflicht neu");
        changed.Fields["premium"] = "70";

        await _member.PutAsJsonAsync($"/api/objects/{item.Id}", new ObjectSaveRequest("insurance", changed, null, _memberPerson, item.Version), Ct);

        var history = (await _guest.GetFromJsonAsync<List<ObjectHistoryDto>>($"/api/objects/{item.Id}/history", Ct))!;
        Assert.Contains(history, h => h.Field == "field:premium" && h.OldValue == "65.5" && h.NewValue == "70" && h.ActorName.StartsWith("Bea", StringComparison.Ordinal));
        Assert.Contains(history, h => h.Field == "title" && h.NewValue == "Haftpflicht neu");
        Assert.Contains(history, h => h.Field == "responsible" && h.NewValue == _memberPerson.ToString());
    }

    [Fact]
    public async Task DeletedObjectsGoToTheTrashAndCanBeRestored()
    {
        var item = await CreateAsync(_admin, new ObjectSaveRequest("insurance", Insurance("Hausrat"), null, _adminPerson));

        Assert.Equal(HttpStatusCode.Forbidden, (await _guest.DeleteAsync(new Uri($"/api/objects/{item.Id}", UriKind.Relative), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _member.DeleteAsync(new Uri($"/api/objects/{item.Id}", UriKind.Relative), Ct)).StatusCode);

        Assert.DoesNotContain(await _admin.GetFromJsonAsync<List<ObjectSummaryDto>>("/api/objects", Ct) ?? [], o => o.Id == item.Id);
        Assert.Contains(await _admin.GetFromJsonAsync<List<ObjectSummaryDto>>("/api/objects/trash", Ct) ?? [], o => o.Id == item.Id);
        var inTrash = (await _admin.GetFromJsonAsync<ObjectDto>($"/api/objects/{item.Id}", Ct))!;
        Assert.NotNull(inTrash.DeletedAt);
        Assert.False(inTrash.CanEdit);

        Assert.Equal(HttpStatusCode.OK, (await _admin.PostAsync(new Uri($"/api/objects/{item.Id}/restore", UriKind.Relative), null, Ct)).StatusCode);
        Assert.Contains(await _guest.GetFromJsonAsync<List<ObjectSummaryDto>>("/api/objects", Ct) ?? [], o => o.Id == item.Id);
    }

    [Fact]
    public async Task StorageLocationsKeepReferenceAndName()
    {
        var place = await CreateAsync(_admin, new ObjectSaveRequest("location", new ObjectData { Title = "Ordner Arbeitszimmer" }, null, _adminPerson));
        var box = new ObjectData { Title = "Schließfach", Fields = new() { ["bank"] = "Sparkasse" } };
        box.SetLocation("keyLocation", place.Id, place.Data.Title);

        var created = await CreateAsync(_admin, new ObjectSaveRequest("safeDeposit", box, null, _adminPerson));

        Assert.Equal(("Ordner Arbeitszimmer", place.Id), (created.Data.Get("keyLocation"), created.Data.Locations["keyLocation"]));
    }
}
