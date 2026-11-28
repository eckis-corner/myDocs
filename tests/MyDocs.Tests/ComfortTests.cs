using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Inbox;
using MyDocs.Server.Infrastructure.Tasks;
using MyDocs.Shared.Documents;
using MyDocs.Shared.Household;
using MyDocs.Shared.Objects;
using MyDocs.Shared.Payments;
using MyDocs.Shared.Tasks;

namespace MyDocs.Tests;

/// <summary>M6: Zahlungen mit Haushaltssicht, Aufbewahrung und Aufräumen, Zuordnungsvorschläge.</summary>
public sealed class ComfortTests(ServerFixture server) : IClassFixture<ServerFixture>, IAsyncLifetime
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
        _bea = await TestAccounts.CreateAsync(server, _admin, $"m6-bea{n}@example.test", $"Bea {n}", HouseholdRoles.Member);
        _carl = await TestAccounts.CreateAsync(server, _admin, $"m6-carl{n}@example.test", $"Carl {n}", HouseholdRoles.Member);
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

    private static async Task<ObjectDto> CreateAsync(HttpClient client, string type, string title, Dictionary<string, string> fields, Guid? owner, Guid? responsible)
    {
        var response = await client.PostAsJsonAsync("/api/objects", new ObjectSaveRequest(type, new ObjectData { Title = title, Fields = fields }, owner, responsible), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ObjectDto>(Ct))!;
    }

    private static async Task<DocumentDto> UploadAsync(HttpClient client, Guid objectId, string kind, DateOnly date)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF " + Guid.NewGuid())), "file", "beleg.pdf");
        var response = await client.PostAsync(new Uri($"/api/objects/{objectId}/documents", UriKind.Relative), form, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = (await response.Content.ReadFromJsonAsync<DocumentDto>(Ct))!;
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/documents/{document.Id}", new DocumentUpdateRequest(document.Title, date, null) { Kind = kind }, Ct)).StatusCode);
        return (await client.GetFromJsonAsync<DocumentDto>($"/api/documents/{document.Id}", Ct))!;
    }

    private async Task RunReviewAsync()
    {
        await using var scope = server.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReviewEngine>().RunAsync(Ct);
    }

    private async Task<WorkTask?> CleanupTaskAsync(Guid person)
    {
        await using var scope = server.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MyDocsDbContext>().Tasks.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Kind == TaskKinds.Cleanup && t.ResponsiblePersonId == person, Ct);
    }

    [Fact]
    public async Task PaymentsShowMineOrTheHouseholdWithAccountsAndCancellations()
    {
        var account = await CreateAsync(_bea, "bankAccount", "Girokonto", new() { ["bank"] = "Sparkasse" }, null, _beaPerson);
        await CreateAsync(_bea, "insurance", "Hausrat", new() { ["insuranceKind"] = "household", ["insurer"] = "HUK", ["premium"] = "120", ["frequency"] = "yearly", ["paymentAccount"] = account.Id.ToString() }, null, _beaPerson);
        var mobile = await CreateAsync(_bea, "contract", "Handy", new() { ["contractKind"] = "mobile", ["provider"] = "Telekom", ["cost"] = "20", ["frequency"] = "monthly" }, _beaPerson, _beaPerson);
        await new MyDocs.ApiClient.TaskClient(_bea).ChangeLifecycleAsync(mobile.Id, new LifecycleChangeRequest(LifecycleStates.Cancelled, DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(2), null, mobile.Version), Ct);
        var ended = await CreateAsync(_bea, "contract", "Alter Strom", new() { ["contractKind"] = "electricity", ["provider"] = "X", ["cost"] = "50", ["frequency"] = "monthly" }, _beaPerson, _beaPerson);
        await new MyDocs.ApiClient.TaskClient(_bea).ChangeLifecycleAsync(ended.Id, new LifecycleChangeRequest(LifecycleStates.Ended, DateOnly.FromDateTime(DateTime.UtcNow), null, ended.Version), Ct);
        await CreateAsync(_carl, "contract", "Carls Gym", new() { ["contractKind"] = "other", ["provider"] = "Gym", ["cost"] = "30", ["frequency"] = "monthly" }, _carlPerson, _carlPerson);
        await CreateAsync(_carl, "contract", "Internet", new() { ["contractKind"] = "internet", ["provider"] = "1&1", ["cost"] = "40", ["frequency"] = "monthly" }, null, _carlPerson);

        var mine = (await _bea.GetFromJsonAsync<List<PaymentDto>>("/api/payments?scope=mine", Ct))!;
        Assert.Equal(["Handy", "Hausrat"], mine.Select(p => p.Title).Order());
        var premium = mine.Single(p => p.Title == "Hausrat");
        Assert.Equal("Girokonto", premium.AccountTitle);
        Assert.Equal(10m, premium.Monthly);
        Assert.NotNull(mine.Single(p => p.Title == "Handy").EndsOn);
        Assert.Equal(30m, PaymentMath.Monthly(mine));
        Assert.Equal(10m, PaymentMath.Monthly(mine.Where(p => !p.Cancelled)));

        // Haushalt: auch was Carl für den Haushalt führt, nie Carls private Einträge.
        var household = (await _bea.GetFromJsonAsync<List<PaymentDto>>("/api/payments?scope=household", Ct))!;
        Assert.Contains(household, p => p.Title == "Internet" && p.ResponsibleName!.StartsWith("Carl", StringComparison.Ordinal) && p.Household);
        Assert.DoesNotContain(household, p => p.Title is "Carls Gym" or "Alter Strom");
    }

    [Fact]
    public async Task ExpiredRetentionGivesACleanupTaskAndKeepingClosesIt()
    {
        var item = await CreateAsync(_bea, "insurance", "Haftpflicht", new() { ["insuranceKind"] = "liability", ["insurer"] = "HUK" }, _beaPerson, _beaPerson);
        var invoice = await UploadAsync(_bea, item.Id, "invoice", new DateOnly(2020, 3, 1));
        var policy = await UploadAsync(_bea, item.Id, "policy", new DateOnly(2015, 1, 1));
        Assert.Equal(new DateOnly(2022, 12, 31), invoice.RetainUntil);
        // Die Police bleibt, solange der Eintrag läuft.
        Assert.Null(policy.RetainUntil);

        await RunReviewAsync();
        var task = await CleanupTaskAsync(_beaPerson);
        Assert.NotNull(task);
        Assert.Equal(TaskStates.Open, task.State);
        var due = (await _bea.GetFromJsonAsync<List<CleanupItemDto>>("/api/cleanup", Ct))!;
        var row = Assert.Single(due);
        Assert.Equal(invoice.Id, row.Id);
        Assert.Equal(RetentionBasis.EndOfYear, row.Basis);
        Assert.Empty((await _carl.GetFromJsonAsync<List<CleanupItemDto>>("/api/cleanup", Ct))!);
        Assert.Equal(HttpStatusCode.NotFound, (await _carl.PutAsJsonAsync($"/api/documents/{invoice.Id}/retention", new RetentionRequest(null, true), Ct)).StatusCode);

        // Nichts wird gelöscht; „Dauerhaft behalten“ erledigt den Vorgang.
        Assert.Equal(HttpStatusCode.NoContent, (await _bea.PutAsJsonAsync($"/api/documents/{invoice.Id}/retention", new RetentionRequest(null, true), Ct)).StatusCode);
        Assert.Empty((await _bea.GetFromJsonAsync<List<CleanupItemDto>>("/api/cleanup", Ct))!);
        var kept = (await _bea.GetFromJsonAsync<DocumentDto>($"/api/documents/{invoice.Id}", Ct))!;
        Assert.Equal(RetentionRules.Forever, kept.RetainUntil);
        Assert.True(kept.RetentionManual);
        await RunReviewAsync();
        Assert.Equal(TaskStates.Done, (await CleanupTaskAsync(_beaPerson))!.State);

        // Nach dem Ende des Eintrags läuft die Frist der Police.
        await new MyDocs.ApiClient.TaskClient(_bea).ChangeLifecycleAsync(item.Id, new LifecycleChangeRequest(LifecycleStates.Ended, new DateOnly(2021, 6, 30), null, item.Version), Ct);
        Assert.Equal(new DateOnly(2024, 6, 30), (await _bea.GetFromJsonAsync<DocumentDto>($"/api/documents/{policy.Id}", Ct))!.RetainUntil);
    }

    [Fact]
    public void NumbersAndIbansInTheTextSuggestEntries()
    {
        static DocObject Item(string type, string title, Dictionary<string, string> fields) =>
            new() { Id = Guid.NewGuid(), TypeKey = type, Title = title, Fields = fields };
        var policy = Item("insurance", "Haftpflicht", new() { ["insuranceKind"] = "liability", ["insurer"] = "HUK", ["policyNumber"] = "HV-123 456 78" });
        var account = Item("bankAccount", "Girokonto", new() { ["bank"] = "Sparkasse", ["iban"] = "DE89370400440532013000" });
        var shortNumber = Item("contract", "Kurz", new() { ["contractKind"] = "mobile", ["provider"] = "X", ["customerNumber"] = "123" });
        const string text = "Ihre Versicherung Nr. hv12345678 – Beitrag wird abgebucht von DE89 3704 0044 0532 0130 00. Kundennummer 123.";

        var suggestions = Suggestions.For(text, [policy, account, shortNumber]);

        Assert.Equal([account.Id, policy.Id], suggestions.Select(s => s.ObjectId));
        Assert.Equal(["iban"], suggestions[0].Fields);
        Assert.Equal(["policyNumber"], suggestions[1].Fields);
        Assert.Empty(Suggestions.For(null, [policy]));
    }
}
