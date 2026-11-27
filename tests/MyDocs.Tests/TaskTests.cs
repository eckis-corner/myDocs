using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using MyCommon.Catalog;
using MyDocs.Shared.Household;
using MyDocs.Shared.Objects;
using MyDocs.Shared.Tasks;

namespace MyDocs.Tests;

/// <summary>Lebenszyklus, Folgevorgänge, Vorgänge und Kommentare (M2.1).</summary>
public sealed partial class TaskTests(ServerFixture server) : IClassFixture<ServerFixture>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static int _counter;
    private HttpClient _admin = null!;
    private HttpClient _member = null!;
    private HttpClient _guest = null!;
    private Guid _adminPerson;
    private Guid _memberPerson;

    public async ValueTask InitializeAsync()
    {
        var n = Interlocked.Increment(ref _counter);
        _admin = await TestAccounts.AdminAsync(server);
        _member = await TestAccounts.CreateAsync(server, _admin, $"task-bea{n}@example.test", $"Bea {n}", HouseholdRoles.Member);
        _guest = await TestAccounts.CreateAsync(server, _admin, $"task-opa{n}@example.test", $"Opa {n}", HouseholdRoles.Guest);
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

    private static async Task<ObjectDto> CreateAsync(HttpClient client, string type, string title, Dictionary<string, string> fields, Guid? owner, Guid? responsible)
    {
        var response = await client.PostAsJsonAsync("/api/objects", new ObjectSaveRequest(type, new ObjectData { Title = title, Fields = fields }, owner, responsible), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ObjectDto>(Ct))!;
    }

    private static Task<ObjectDto> ContractAsync(HttpClient client, Guid responsible) =>
        CreateAsync(client, "contract", "Fitnessstudio", new() { ["contractKind"] = "gym", ["provider"] = "FitX" }, null, responsible);

    private static async Task<HttpResponseMessage> ChangeAsync(HttpClient client, ObjectDto item, string state, DateOnly? date = null, Guid? replacedBy = null) =>
        await client.PostAsJsonAsync($"/api/objects/{item.Id}/lifecycle", new LifecycleChangeRequest(state, date, replacedBy, item.Version), Ct);

    [Fact]
    public async Task NewEntriesStartInTheFirstStateOfTheirPattern()
    {
        Assert.Equal(LifecycleStates.Active, (await ContractAsync(_admin, _adminPerson)).LifecycleState);
        Assert.Equal(LifecycleStates.Valid, (await CreateAsync(_admin, "idDocument", "Ausweis", new() { ["idDocumentKind"] = "idCard" }, null, _adminPerson)).LifecycleState);
        Assert.Equal(LifecycleStates.Present, (await CreateAsync(_admin, "vehicle", "Auto", new() { ["vehicleKind"] = "car", ["makeModel"] = "Kombi" }, null, _adminPerson)).LifecycleState);
        Assert.Null((await CreateAsync(_admin, "contact", "Steuerbüro", new() { ["name"] = "Frau Klein" }, null, _adminPerson)).LifecycleState);
    }

    [Fact]
    public async Task CancellingCreatesTheFollowUpOnce()
    {
        var gym = await ContractAsync(_admin, _memberPerson);

        var withoutDate = await ChangeAsync(_admin, gym, LifecycleStates.Cancelled);
        Assert.Equal(ObjectErrors.LifecycleDateRequired, await SetupTests.TitleAsync(withoutDate));
        var invalid = await ChangeAsync(_admin, gym, LifecycleStates.Valid);
        Assert.Equal(ObjectErrors.LifecycleInvalid, await SetupTests.TitleAsync(invalid));

        var cancelled = (await (await ChangeAsync(_admin, gym, LifecycleStates.Cancelled, new DateOnly(2027, 3, 31))).Content.ReadFromJsonAsync<ObjectDto>(Ct))!;
        Assert.Equal((LifecycleStates.Cancelled, new DateOnly(2027, 3, 31)), (cancelled.LifecycleState, cancelled.LifecycleDate));

        var tasks = (await _member.GetFromJsonAsync<List<TaskDto>>($"/api/objects/{gym.Id}/tasks", Ct))!;
        var followUp = Assert.Single(tasks);
        Assert.Equal(("cancellation-confirmation", _memberPerson, TaskStates.Open), (followUp.TitleKey, followUp.ResponsiblePersonId, followUp.State));

        // Zurück auf aktiv und wieder gekündigt: kein zweiter offener Vorgang.
        var active = (await (await ChangeAsync(_admin, cancelled, LifecycleStates.Active)).Content.ReadFromJsonAsync<ObjectDto>(Ct))!;
        await ChangeAsync(_admin, active, LifecycleStates.Cancelled, new DateOnly(2027, 6, 30));
        Assert.Single((await _member.GetFromJsonAsync<List<TaskDto>>($"/api/objects/{gym.Id}/tasks", Ct))!);
        Assert.Contains((await _member.GetFromJsonAsync<List<TaskDto>>("/api/tasks?scope=mine", Ct))!, t => t.Id == followUp.Id);
    }

    [Fact]
    public async Task ArchivedEntriesLeaveTheListsButNotTheArchive()
    {
        var gym = await ContractAsync(_admin, _adminPerson);
        var ended = (await (await ChangeAsync(_admin, gym, LifecycleStates.Ended, new DateOnly(2026, 12, 31))).Content.ReadFromJsonAsync<ObjectDto>(Ct))!;
        Assert.Equal(2, (await _admin.GetFromJsonAsync<List<TaskDto>>($"/api/objects/{gym.Id}/tasks", Ct))!.Count);

        await ChangeAsync(_admin, ended, LifecycleStates.Archived);

        Assert.DoesNotContain(await _admin.GetFromJsonAsync<List<ObjectSummaryDto>>("/api/objects?area=5", Ct) ?? [], o => o.Id == gym.Id);
        Assert.Contains(await _admin.GetFromJsonAsync<List<ObjectSummaryDto>>("/api/objects?archived=true", Ct) ?? [], o => o.Id == gym.Id);
    }

    [Fact]
    public async Task ReplacingNeedsASuccessorOfTheSameType()
    {
        var old = await CreateAsync(_admin, "idDocument", "Reisepass alt", new() { ["idDocumentKind"] = "passport" }, null, _adminPerson);
        var contract = await ContractAsync(_admin, _adminPerson);
        Assert.Equal(ObjectErrors.ReplacementInvalid, await SetupTests.TitleAsync(await ChangeAsync(_admin, old, LifecycleStates.Replaced, replacedBy: contract.Id)));

        var successor = await CreateAsync(_admin, "idDocument", "Reisepass neu", new() { ["idDocumentKind"] = "passport" }, null, _adminPerson);
        var replaced = (await (await ChangeAsync(_admin, old, LifecycleStates.Replaced, replacedBy: successor.Id)).Content.ReadFromJsonAsync<ObjectDto>(Ct))!;

        Assert.Equal(successor.Id, replaced.ReplacedById);
    }

    [Fact]
    public async Task SellingAVehicleListsTheContractsThatPointToIt()
    {
        var car = await CreateAsync(_admin, "vehicle", "Familienauto", new() { ["vehicleKind"] = "car", ["makeModel"] = "Kombi" }, null, _adminPerson);
        var insurance = await CreateAsync(_admin, "insurance", "Kfz-Versicherung", new() { ["insuranceKind"] = "car", ["insurer"] = "HUK", ["vehicle"] = car.Id.ToString() }, null, _adminPerson);

        await ChangeAsync(_admin, car, LifecycleStates.Disposed, new DateOnly(2026, 12, 1));

        var task = Assert.Single((await _admin.GetFromJsonAsync<List<TaskDto>>($"/api/objects/{car.Id}/tasks", Ct))!);
        Assert.Equal("check-related-contracts", task.TitleKey);
        Assert.Contains(insurance.Id, task.ObjectIds);
    }

    [Fact]
    public async Task AManualTaskWaitsOnlyWithADateAndClosesWithoutOutcome()
    {
        var gym = await ContractAsync(_admin, _adminPerson);

        // Eine Aufgabe von Hand: Abschluss ohne Ergebnis ist erlaubt (das Ergebnis ist bei Prüfung und Frist Pflicht, M2.2).
        var manual = (await (await _member.PostAsJsonAsync("/api/tasks", new TaskSaveRequest("Beim Versicherer Fahrzeugwechsel melden", null, _memberPerson, new DateOnly(2026, 12, 15), TaskPriorities.High, [gym.Id]), Ct))
            .Content.ReadFromJsonAsync<TaskDto>(Ct))!;
        Assert.Equal((TaskKinds.Task, TaskPriorities.High), (manual.Kind, manual.Priority));

        var waiting = await _member.PostAsJsonAsync($"/api/tasks/{manual.Id}/state", new TaskStateRequest(TaskStates.Waiting, null, null, null, manual.Version), Ct);
        Assert.Equal(TaskErrors.WaitUntilRequired, await SetupTests.TitleAsync(waiting));
        var waited = (await (await _member.PostAsJsonAsync($"/api/tasks/{manual.Id}/state",
            new TaskStateRequest(TaskStates.Waiting, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10), null, null, manual.Version), Ct)).Content.ReadFromJsonAsync<TaskDto>(Ct))!;
        Assert.NotNull(waited.WaitUntil);

        var done = await _member.PostAsJsonAsync($"/api/tasks/{manual.Id}/state", new TaskStateRequest(TaskStates.Done, null, null, "gemeldet", waited.Version), Ct);
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        var closed = (await done.Content.ReadFromJsonAsync<TaskDto>(Ct))!;
        Assert.Equal((TaskStates.Done, "gemeldet"), (closed.State, closed.OutcomeNote));
        Assert.NotNull(closed.ClosedByName);
        Assert.DoesNotContain((await _member.GetFromJsonAsync<List<TaskDto>>("/api/tasks", Ct))!, t => t.Id == manual.Id);
        Assert.Contains((await _member.GetFromJsonAsync<List<TaskDto>>("/api/tasks?open=false", Ct))!, t => t.Id == manual.Id);
    }

    [Fact]
    public async Task GuestsReadTasksButDoNotChangeThem()
    {
        var gym = await ContractAsync(_admin, _adminPerson);
        var task = (await (await _admin.PostAsJsonAsync("/api/tasks", new TaskSaveRequest("Vertrag prüfen", null, _adminPerson, null, TaskPriorities.Normal, [gym.Id]), Ct))
            .Content.ReadFromJsonAsync<TaskDto>(Ct))!;

        var seen = (await _guest.GetFromJsonAsync<TaskDto>($"/api/tasks/{task.Id}", Ct))!;
        Assert.False(seen.CanEdit);
        Assert.Equal(HttpStatusCode.Forbidden, (await _guest.PostAsJsonAsync($"/api/tasks/{task.Id}/state", new TaskStateRequest(TaskStates.Done, null, null, null, task.Version), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _guest.PostAsJsonAsync("/api/tasks", new TaskSaveRequest("x", null, null, null, TaskPriorities.Normal, []), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _guest.PostAsJsonAsync($"/api/objects/{gym.Id}/lifecycle", new LifecycleChangeRequest(LifecycleStates.Ended, new DateOnly(2027, 1, 1), null, gym.Version), Ct)).StatusCode);
    }

    [Fact]
    public async Task TasksOnPrivateEntriesStayPrivate()
    {
        var mine = await CreateAsync(_member, "insurance", "BU privat", new() { ["insuranceKind"] = "disability", ["insurer"] = "Alte Leipziger" }, _memberPerson, null);
        var task = (await (await _member.PostAsJsonAsync("/api/tasks", new TaskSaveRequest("Beitrag vergleichen", null, _memberPerson, null, TaskPriorities.Normal, [mine.Id]), Ct))
            .Content.ReadFromJsonAsync<TaskDto>(Ct))!;

        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync(new Uri($"/api/tasks/{task.Id}", UriKind.Relative), Ct)).StatusCode);
        Assert.DoesNotContain((await _admin.GetFromJsonAsync<List<TaskDto>>("/api/tasks", Ct))!, t => t.Id == task.Id);
        // Einen Vorgang an einen fremden privaten Eintrag hängen geht nicht.
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.PostAsJsonAsync("/api/tasks", new TaskSaveRequest("x", null, null, null, TaskPriorities.Normal, [mine.Id]), Ct)).StatusCode);
    }

    [Fact]
    public async Task CommentsBelongToTasksAndEntries()
    {
        var gym = await ContractAsync(_admin, _adminPerson);
        var task = (await (await _admin.PostAsJsonAsync("/api/tasks", new TaskSaveRequest("Kündigen?", null, _adminPerson, null, TaskPriorities.Normal, [gym.Id]), Ct))
            .Content.ReadFromJsonAsync<TaskDto>(Ct))!;

        var comment = (await (await _member.PostAsJsonAsync($"/api/tasks/{task.Id}/comments", new CommentRequest("Habe angerufen, Rückruf Montag"), Ct)).Content.ReadFromJsonAsync<CommentDto>(Ct))!;
        await _admin.PostAsJsonAsync($"/api/objects/{gym.Id}/comments", new CommentRequest("Vertrag liegt im Ordner"), Ct);

        Assert.Equal("Habe angerufen, Rückruf Montag", Assert.Single((await _guest.GetFromJsonAsync<List<CommentDto>>($"/api/tasks/{task.Id}/comments", Ct))!).Text);
        Assert.Single((await _guest.GetFromJsonAsync<List<CommentDto>>($"/api/objects/{gym.Id}/comments", Ct))!);
        Assert.Equal(HttpStatusCode.Forbidden, (await _guest.PostAsJsonAsync($"/api/tasks/{task.Id}/comments", new CommentRequest("Gast"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.DeleteAsync(new Uri($"/api/comments/{comment.Id}", UriKind.Relative), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _member.DeleteAsync(new Uri($"/api/comments/{comment.Id}", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public void TheMigrationKnowsTheSamePatternsAsTheCode()
    {
        var migration = File.ReadAllText(Directory.GetFiles(Path.Combine(ResourceParityTests.FindRepositoryRoot(), "src", "MyDocs.Server", "Data", "Migrations"), "*_Tasks.cs").Single());
        foreach (Match match in StateList().Matches(migration))
        {
            var state = match.Groups["state"].Value;
            foreach (var type in match.Groups["types"].Value.Split(',', StringSplitOptions.TrimEntries).Select(t => t.Trim('\'')))
            {
                Assert.Equal(state, Lifecycle.InitialState(type));
            }
        }

        var listed = StateList().Matches(migration).SelectMany(m => m.Groups["types"].Value.Split(',', StringSplitOptions.TrimEntries).Select(t => t.Trim('\''))).ToHashSet();
        Assert.All(ObjectCatalog.Types.Where(t => Lifecycle.PatternOf(t.Key) != LifecyclePattern.None), t => Assert.Contains(t.Key, listed));
    }

    [GeneratedRegex(@"WHEN type_key IN \((?<types>[^)]*)\) THEN '(?<state>\w+)'")]
    private static partial Regex StateList();
}
