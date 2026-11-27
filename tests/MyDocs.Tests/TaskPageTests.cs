using System.Globalization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MyDocs.ApiClient;
using MyDocs.Shared.Auth;
using MyDocs.Shared.Household;
using MyDocs.Shared.Objects;
using MyDocs.Shared.Tasks;
using MyDocs.UI;
using MyDocs.UI.Pages;

namespace MyDocs.Tests;

public sealed class TaskPageTests : BunitContext
{
    private static readonly Guid Anna = Guid.NewGuid();
    private readonly FakeApi _api = new();

    public TaskPageTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMyDocsUi();
        Services.AddMyDocsApi(new Uri("http://localhost/"));
        Services.AddScoped(_ => new HttpClient(_api) { BaseAddress = new Uri("http://localhost/") });
        AddAuthorization().SetAuthorized("Anna").SetRoles(HouseholdRoles.Member);
        _api.OnJson("GET /api/auth/me", new MeDto(Guid.NewGuid(), "anna@example.test", "Anna", false, HouseholdRoles.Member, Anna, 10))
            .OnJson("GET /api/persons", new List<PersonDto> { new(Anna, "Anna", null, Guid.NewGuid(), null) })
            .OnJson("GET /api/objects", new List<ObjectSummaryDto>());
    }

    private static TaskDto Task(string kind, string title, string? key, DateOnly? due, string state = TaskStates.Open) =>
        new(Guid.NewGuid(), kind, title, null, Anna, due, TaskPriorities.Normal, state, null, null, null, [], true, DateTimeOffset.UtcNow, "Anna", null, null, 1) { TitleKey = key };

    [Fact]
    public void TasksShowTranslatedTitlesAndOverdueOnes()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
        _api.OnJson("GET /api/tasks", new List<TaskDto>
        {
            Task(TaskKinds.Task, "Kündigungsbestätigung ablegen", "cancellation-confirmation", DateOnly.FromDateTime(DateTime.Today).AddDays(-2)),
            Task(TaskKinds.Task, "Beim Versicherer anrufen", null, null),
        });

        var page = Render<TasksPage>();

        page.WaitForAssertion(() => Assert.Contains("File the cancellation confirmation", page.Markup, StringComparison.Ordinal));
        Assert.Contains("Beim Versicherer anrufen", page.Markup, StringComparison.Ordinal);
        Assert.Contains("overdue", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AReviewNeedsAnOutcomeBeforeItCanBeClosed()
    {
        var review = Task(TaskKinds.Review, "Versicherung prüfen", null, DateOnly.FromDateTime(DateTime.Today));
        _api.OnJson("GET /api/tasks", new List<TaskDto> { review })
            .OnJson($"GET /api/tasks/{review.Id}/comments", new List<CommentDto>());
        var page = Render<TasksPage>();
        var provider = Render<MudDialogProvider>();
        page.WaitForAssertion(() => Assert.Contains("Versicherung prüfen", page.Markup, StringComparison.Ordinal));

        page.Find("[data-testid=tasks] tbody tr").Click();
        provider.WaitForElement("[data-testid=task-close]").Click();

        provider.WaitForElement("[data-testid=task-closing]");
        Assert.True(provider.Find("[data-testid=task-done]").HasAttribute("disabled"));
    }

    [Fact]
    public void TheReviewPanelShowsOverdueAndTheDeadline()
    {
        var item = new ObjectDto(Guid.NewGuid(), "insurance", new ObjectData { Title = "Hausrat" }, null, Anna, [], CanEdit: true, CanManage: true, Version: 1,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, LifecycleStates.Active)
        {
            Review = new ReviewInfoDto(12, false, new DateOnly(2026, 11, 1), false, new DateOnly(2025, 11, 1), new DateOnly(2026, 11, 15), new DateOnly(2026, 12, 1), "cancellation-deadline"),
        };

        var panel = Render<MyDocs.UI.Components.ReviewPanel>(p => p.Add(r => r.Item, item));

        Assert.Contains("überfällig seit 15.11.2026", panel.Find("[data-testid=review-overdue]").TextContent, StringComparison.Ordinal);
        Assert.Contains("01.12.2026", panel.Find("[data-testid=review-deadline]").TextContent, StringComparison.Ordinal);
        Assert.Contains("alle 12 Monate", panel.Find("[data-testid=review-next]").TextContent, StringComparison.Ordinal);
        Assert.Contains("Letzter Kündigungstermin", panel.Markup, StringComparison.Ordinal);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _api.Dispose();
        }

        base.Dispose(disposing);
    }
}
