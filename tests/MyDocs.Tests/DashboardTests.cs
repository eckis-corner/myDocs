using System.Net;
using System.Net.Http.Json;
using MyDocs.Shared.Dashboard;
using MyDocs.Shared.Household;
using MyDocs.Shared.Objects;
using MyDocs.Shared.Tasks;

namespace MyDocs.Tests;

/// <summary>Startseite (M2.3): Vorgänge, Fristen, Zahlungen, Zustand des Bestands, „Meins“ und „Haushalt“.</summary>
public sealed class DashboardTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateOnly Today => DateOnly.FromDateTime(server.Clock.GetUtcNow().UtcDateTime);

    private static async Task<ObjectDto> CreateAsync(HttpClient client, string type, string title, Dictionary<string, string> fields, Guid? owner, Guid? responsible, DateOnly? expires = null)
    {
        var response = await client.PostAsJsonAsync("/api/objects", new ObjectSaveRequest(type, new ObjectData { Title = title, Fields = fields, ExpiresOn = expires }, owner, responsible), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ObjectDto>(Ct))!;
    }

    [Fact]
    public async Task TheDashboardSumsPaymentsAndCollectsDeadlinesAndTasks()
    {
        using var admin = await TestAccounts.AdminAsync(server);
        using var member = await TestAccounts.CreateAsync(server, admin, $"dash-{Guid.NewGuid():N}@example.test", "Bea", HouseholdRoles.Member);
        var me = (await TestAccounts.MeAsync(admin)).PersonId!.Value;
        var bea = (await TestAccounts.MeAsync(member)).PersonId!.Value;

        await CreateAsync(admin, "insurance", "Haftpflicht", new() { ["insuranceKind"] = "liability", ["insurer"] = "HUK", ["premium"] = "120", ["frequency"] = "yearly", ["policyNumber"] = "P-1" }, null, me);
        var gym = await CreateAsync(admin, "contract", "Fitnessstudio", new() { ["contractKind"] = "gym", ["provider"] = "FitX", ["cost"] = "30", ["frequency"] = "monthly" }, null, me);
        await CreateAsync(admin, "loan", "Darlehen an Tom", new() { ["loanDirection"] = "owedToMe", ["counterparty"] = "Tom", ["installment"] = "100", ["frequency"] = "monthly" }, null, me);
        await CreateAsync(admin, "card", "Kreditkarte", new() { ["cardKind"] = "visa", ["annualFee"] = "24" }, null, bea, Today.AddDays(40));
        await CreateAsync(member, "contract", "Streaming privat", new() { ["contractKind"] = "streaming", ["provider"] = "Kino", ["cost"] = "15", ["frequency"] = "monthly" }, bea, null);
        await admin.PostAsJsonAsync($"/api/objects/{gym.Id}/lifecycle", new LifecycleChangeRequest(LifecycleStates.Cancelled, Today.AddDays(20), null, gym.Version), Ct);
        await admin.PostAsJsonAsync("/api/tasks", new TaskSaveRequest("Vergessene Sache", null, me, Today.AddDays(-3), TaskPriorities.Normal, []), Ct);
        await admin.PostAsJsonAsync("/api/tasks", new TaskSaveRequest("Diese Woche", null, me, Today, TaskPriorities.Normal, []), Ct);

        var household = (await admin.GetFromJsonAsync<DashboardDto>("/api/dashboard?scope=household", Ct))!;

        // 120/12 + 30 + 24/12 = 42 im Monat; der Kredit „mir wird geschuldet“ und das private Abo von Bea zählen nicht.
        Assert.Equal(42m, household.MonthlyTotal);
        Assert.Contains(household.PaymentsByArea, p => p.Area == 4 && p.Monthly == 10m);
        Assert.Contains(household.Overdue, t => t.Title == "Vergessene Sache");
        Assert.Contains(household.DueThisWeek, t => t.Title == "Diese Woche");
        Assert.Contains(household.Deadlines, d => d.Kind == DeadlineKinds.ContractEnd && d.ObjectId == gym.Id && d.Date == Today.AddDays(20));
        Assert.Contains(household.Deadlines, d => d.Kind == DeadlineKinds.Expiry && d.ObjectTitle == "Kreditkarte");
        Assert.Contains(household.Incomplete, i => i.Title == "Fitnessstudio" && i.MissingFields.Contains("customerNumber"));
        Assert.DoesNotContain(household.Incomplete, i => i.Title == "Haftpflicht");
        Assert.Equal(household.Incomplete.Count, household.IncompleteCount);

        var mine = (await admin.GetFromJsonAsync<DashboardDto>("/api/dashboard?scope=mine", Ct))!;
        Assert.DoesNotContain(mine.Deadlines, d => d.ObjectTitle == "Kreditkarte");
        Assert.Equal(40m, mine.MonthlyTotal);

        var beas = (await member.GetFromJsonAsync<DashboardDto>("/api/dashboard?scope=mine", Ct))!;
        Assert.Equal(17m, beas.MonthlyTotal);
    }

    [Fact]
    public async Task EntriesNotReviewedForAYearShowUp()
    {
        using var admin = await TestAccounts.AdminAsync(server);
        var me = (await TestAccounts.MeAsync(admin)).PersonId!.Value;
        await CreateAsync(admin, "bankAccount", "Altes Sparkonto", new() { ["bank"] = "Sparkasse", ["iban"] = "DE89370400440532013000", ["holder"] = "Admin", ["powerOfAttorney"] = "keine" }, null, me);

        server.Clock.Advance(TimeSpan.FromDays(400));
        using var later = await TestAccounts.AdminAsync(server);

        var dashboard = (await later.GetFromJsonAsync<DashboardDto>("/api/dashboard?scope=household", Ct))!;
        Assert.Contains(dashboard.Unreviewed, u => u.Title == "Altes Sparkonto");
    }
}
