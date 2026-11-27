using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Tasks;
using MyDocs.Shared.Objects;
using MyDocs.Shared.Tasks;

namespace MyDocs.Tests;

/// <summary>Wiedervorlage, Fristen, Ablauf, Eskalation, Ergebnis und Zurückstellen (M2.2). Die Uhr des Servers läuft vor.</summary>
public sealed class ReviewTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateOnly Today => DateOnly.FromDateTime(server.Clock.GetUtcNow().UtcDateTime);

    private async Task<ReviewEngine.Result> RunAsync()
    {
        using var scope = server.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ReviewEngine>().RunAsync(Ct);
    }

    private async Task<T> InDbAsync<T>(Func<MyDocsDbContext, Task<T>> query)
    {
        using var scope = server.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<MyDocsDbContext>());
    }

    private static async Task<ObjectDto> CreateAsync(HttpClient client, string type, string title, Dictionary<string, string> fields, Guid responsible, DateOnly? expires = null)
    {
        var response = await client.PostAsJsonAsync("/api/objects", new ObjectSaveRequest(type, new ObjectData { Title = title, Fields = fields, ExpiresOn = expires }, null, responsible), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ObjectDto>(Ct))!;
    }

    private Task<List<WorkTask>> TasksOfAsync(Guid objectId) =>
        InDbAsync(db => db.Tasks.Where(t => db.TaskObjects.Any(l => l.TaskId == t.Id && l.ObjectId == objectId)).ToListAsync(Ct));

    [Fact]
    public async Task AReviewIsDueAfterTheIntervalAndClosesOnlyWithAnOutcome()
    {
        using var admin = await TestAccounts.AdminAsync(server);
        var person = (await TestAccounts.MeAsync(admin)).PersonId!.Value;
        var policy = await CreateAsync(admin, "insurance", "Privathaftpflicht", new() { ["insuranceKind"] = "liability", ["insurer"] = "HUK" }, person);
        Assert.Equal(Today.AddMonths(12), policy.Review!.NextReviewOn);

        await RunAsync();
        Assert.DoesNotContain(await TasksOfAsync(policy.Id), t => t.Kind == TaskKinds.Review);

        server.Clock.Advance(TimeSpan.FromDays(366));
        await RunAsync();
        await RunAsync();
        var review = Assert.Single(await TasksOfAsync(policy.Id), t => t.Kind == TaskKinds.Review);
        Assert.Equal((person, 1), (review.ResponsiblePersonId, review.EscalationLevel));

        // Eskalation: nach 7 Tagen Stufe 2, nach 14 Tagen Stufe 3 und „überfällig“.
        server.Clock.Advance(TimeSpan.FromDays(7));
        await RunAsync();
        Assert.Equal(2, (await InDbAsync(db => db.Tasks.SingleAsync(t => t.Id == review.Id, Ct))).EscalationLevel);
        server.Clock.Advance(TimeSpan.FromDays(7));
        await RunAsync();
        Assert.Equal(3, (await InDbAsync(db => db.Tasks.SingleAsync(t => t.Id == review.Id, Ct))).EscalationLevel);
        Assert.NotNull((await InDbAsync(db => db.Objects.SingleAsync(o => o.Id == policy.Id, Ct))).OverdueSince);

        using var again = await TestAccounts.AdminAsync(server);
        var dto = (await again.GetFromJsonAsync<TaskDto>($"/api/tasks/{review.Id}", Ct))!;
        var withoutOutcome = await again.PostAsJsonAsync($"/api/tasks/{review.Id}/state", new TaskStateRequest(TaskStates.Done, null, null, null, dto.Version), Ct);
        Assert.Equal(TaskErrors.OutcomeRequired, await SetupTests.TitleAsync(withoutOutcome));
        var done = await again.PostAsJsonAsync($"/api/tasks/{review.Id}/state", new TaskStateRequest(TaskStates.Done, null, TaskOutcomes.Unchanged, "Beitrag gleich", dto.Version), Ct);
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);

        var reviewed = (await again.GetFromJsonAsync<ObjectDto>($"/api/objects/{policy.Id}", Ct))!;
        Assert.Equal(Today, reviewed.Review!.LastReviewedOn);
        Assert.Null(reviewed.Review.OverdueSince);
        Assert.Equal(Today.AddMonths(12), reviewed.Review.NextReviewOn);
        await RunAsync();
        Assert.Single(await TasksOfAsync(policy.Id), t => t.Kind == TaskKinds.Review);
    }

    [Fact]
    public async Task TheCancellationDeadlineComesFromDueDateAndNoticePeriod()
    {
        using var admin = await TestAccounts.AdminAsync(server);
        var person = (await TestAccounts.MeAsync(admin)).PersonId!.Value;
        var due = Today.AddDays(60);
        var policy = await CreateAsync(admin, "insurance", "Hausrat", new()
        {
            ["insuranceKind"] = "household",
            ["insurer"] = "Allianz",
            ["mainDueDate"] = due.ToString("dd.MM.", CultureInfo.InvariantCulture),
            ["noticePeriod"] = "1 Monat",
        }, person);
        Assert.Equal(due.AddMonths(-1), policy.Review!.DeadlineOn);

        await RunAsync();
        await RunAsync();

        var deadline = Assert.Single(await TasksOfAsync(policy.Id), t => t.Kind == TaskKinds.Deadline);
        Assert.Equal((due.AddMonths(-1), "cancellation-deadline"), (deadline.DueOn!.Value, deadline.TitleKey));
    }

    [Fact]
    public async Task IdDocumentsRunOutByThemselves()
    {
        using var admin = await TestAccounts.AdminAsync(server);
        var person = (await TestAccounts.MeAsync(admin)).PersonId!.Value;
        var passport = await CreateAsync(admin, "idDocument", "Reisepass", new() { ["idDocumentKind"] = "passport" }, person, Today.AddDays(30));

        await RunAsync();
        var expiring = await InDbAsync(db => db.Objects.SingleAsync(o => o.Id == passport.Id, Ct));
        Assert.Equal(LifecycleStates.Expiring, expiring.LifecycleState);
        Assert.Contains(await TasksOfAsync(passport.Id), t => t.TitleKey == "renew-or-replace");

        server.Clock.Advance(TimeSpan.FromDays(31));
        await RunAsync();
        Assert.Equal(LifecycleStates.Expired, (await InDbAsync(db => db.Objects.SingleAsync(o => o.Id == passport.Id, Ct))).LifecycleState);
        Assert.Contains(await TasksOfAsync(passport.Id), t => t.TitleKey == "create-replacement");
        Assert.Contains(await InDbAsync(db => db.ObjectHistory.Where(h => h.ObjectId == passport.Id && h.Field == "lifecycle").ToListAsync(Ct)), h => h.NewValue == LifecycleStates.Expired);
    }

    [Fact]
    public async Task WaitingTasksComeBackAndPostponingHasLimits()
    {
        using var admin = await TestAccounts.AdminAsync(server);
        var person = (await TestAccounts.MeAsync(admin)).PersonId!.Value;
        var task = (await (await admin.PostAsJsonAsync("/api/tasks", new TaskSaveRequest("Rückruf der Krankenkasse", null, person, Today, TaskPriorities.Normal, []), Ct)).Content.ReadFromJsonAsync<TaskDto>(Ct))!;

        var tooFar = await admin.PostAsJsonAsync($"/api/tasks/{task.Id}/postpone", new TaskPostponeRequest(Today.AddDays(31), "später", task.Version), Ct);
        Assert.Equal(TaskErrors.PostponeInvalid, await SetupTests.TitleAsync(tooFar));
        var noReason = await admin.PostAsJsonAsync($"/api/tasks/{task.Id}/postpone", new TaskPostponeRequest(Today.AddDays(10), " ", task.Version), Ct);
        Assert.Equal(TaskErrors.ReasonRequired, await SetupTests.TitleAsync(noReason));
        var postponed = (await (await admin.PostAsJsonAsync($"/api/tasks/{task.Id}/postpone", new TaskPostponeRequest(Today.AddDays(10), "Urlaub", task.Version), Ct)).Content.ReadFromJsonAsync<TaskDto>(Ct))!;
        Assert.Equal((Today.AddDays(10), 1), (postponed.DueOn!.Value, postponed.PostponedCount));
        Assert.Contains((await admin.GetFromJsonAsync<List<CommentDto>>($"/api/tasks/{task.Id}/comments", Ct))!, c => c.Text.EndsWith(": Urlaub", StringComparison.Ordinal));

        await admin.PostAsJsonAsync($"/api/tasks/{task.Id}/state", new TaskStateRequest(TaskStates.Waiting, Today.AddDays(2), null, null, postponed.Version), Ct);
        server.Clock.Advance(TimeSpan.FromDays(3));
        await RunAsync();

        Assert.Equal(TaskStates.Open, (await InDbAsync(db => db.Tasks.SingleAsync(t => t.Id == task.Id, Ct))).State);
    }

    [Fact]
    public async Task AnOwnRuleReplacesTheRuleOfTheType()
    {
        using var admin = await TestAccounts.AdminAsync(server);
        var person = (await TestAccounts.MeAsync(admin)).PersonId!.Value;
        var account = await CreateAsync(admin, "bankAccount", "Tagesgeld", new() { ["bank"] = "DKB" }, person);

        var none = (await (await admin.PutAsJsonAsync($"/api/objects/{account.Id}/review", new ReviewSettingsRequest(0, null), Ct)).Content.ReadFromJsonAsync<ObjectDto>(Ct))!;
        Assert.Null(none.Review?.NextReviewOn);

        var fixedDate = (await (await admin.PutAsJsonAsync($"/api/objects/{account.Id}/review", new ReviewSettingsRequest(0, Today.AddDays(3)), Ct)).Content.ReadFromJsonAsync<ObjectDto>(Ct))!;
        Assert.Equal(Today.AddDays(3), fixedDate.Review!.NextReviewOn);

        var now = await admin.PostAsync(new Uri($"/api/objects/{account.Id}/review/now", UriKind.Relative), null, Ct);
        Assert.Equal(HttpStatusCode.OK, now.StatusCode);
        await admin.PostAsync(new Uri($"/api/objects/{account.Id}/review/now", UriKind.Relative), null, Ct);
        Assert.Single(await TasksOfAsync(account.Id), t => t.Kind == TaskKinds.Review);
    }

    [Theory]
    [InlineData("01.01.", 1, 1)]
    [InlineData("1.4.", 4, 1)]
    [InlineData("15.10.2025", 10, 15)]
    [InlineData("1. Januar", 1, 1)]
    [InlineData("1. März", 3, 1)]
    [InlineData("2027-07-01", 7, 1)]
    public void DueDatesAreReadGenerously(string text, int month, int day)
    {
        var today = new DateOnly(2026, 11, 27);
        var next = ReviewRules.NextDueDate(text, today)!.Value;
        Assert.Equal((month, day), (next.Month, next.Day));
        Assert.True(next >= today && next < today.AddYears(1));
    }

    [Theory]
    [InlineData("3 Monate", 3, 0)]
    [InlineData("1 Monat zum Ablauf", 1, 0)]
    [InlineData("6 Wochen", 0, 42)]
    [InlineData("30 Tage", 0, 30)]
    [InlineData("3 months", 3, 0)]
    [InlineData("jederzeit", 0, 0)]
    public void NoticePeriodsAreReadGenerously(string text, int months, int days) =>
        Assert.Equal((months, days), ReviewRules.ParsePeriod(text));

    [Fact]
    public void UnreadableDueDatesGiveNoDeadline() =>
        Assert.Null(ReviewRules.DeadlineFor("insurance", new ObjectData { Fields = new() { ["mainDueDate"] = "jedes Jahr" } }, new DateOnly(2026, 11, 27)));
}
