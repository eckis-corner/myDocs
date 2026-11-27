using System.Globalization;
using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MyDocs.ApiClient;
using MyDocs.Shared.Auth;
using MyDocs.Shared.Household;
using MyDocs.Shared.Objects;
using MyDocs.UI;
using MyDocs.UI.Pages;

namespace MyDocs.Tests;

public sealed class ObjectPageTests : BunitContext
{
    private static readonly Guid Anna = Guid.NewGuid();
    private static readonly Guid AnnaUser = Guid.NewGuid();
    private static readonly Guid PolicyId = Guid.NewGuid();
    private readonly FakeApi _api = new();

    public ObjectPageTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMyDocsUi();
        Services.AddMyDocsApi(new Uri("http://localhost/"));
        Services.AddScoped(_ => new HttpClient(_api) { BaseAddress = new Uri("http://localhost/") });
        AddAuthorization().SetAuthorized("Anna").SetRoles(HouseholdRoles.Member);
        var fields = new Dictionary<string, string> { ["insuranceKind"] = "liability", ["insurer"] = "HUK", ["premium"] = "65.5", ["frequency"] = "yearly" };
        _api.OnJson("GET /api/auth/me", new MeDto(AnnaUser, "anna@example.test", "Anna", false, HouseholdRoles.Member, Anna, 10))
            .OnJson("GET /api/persons", new List<PersonDto> { new(Anna, "Anna", null, AnnaUser, null) })
            .OnJson("GET /api/objects", new List<ObjectSummaryDto> { new(PolicyId, "insurance", "Haftpflicht", null, Anna, null, DateTimeOffset.UtcNow, fields) })
            .OnJson($"GET /api/objects/{PolicyId}", new ObjectDto(PolicyId, "insurance", new ObjectData { Title = "Haftpflicht", Fields = new(fields) }, null, Anna, [],
                CanEdit: true, CanManage: true, Version: 7, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, LifecycleState: "active"))
            .OnJson($"GET /api/objects/{PolicyId}/documents", new List<MyDocs.Shared.Documents.DocumentDto>())
            .OnJson("GET /api/info", new MyDocs.Shared.AppInfoDto("0.1.0", 100))
            .OnJson($"GET /api/objects/{PolicyId}/tasks", new List<MyDocs.Shared.Tasks.TaskDto>())
            .OnJson($"GET /api/objects/{PolicyId}/comments", new List<MyDocs.Shared.Tasks.CommentDto>());
    }

    [Fact]
    public void TheAreaListsItsObjectsWithTheirDetails()
    {
        var page = Render<AreaPage>(p => p.Add(a => a.Number, 4));

        page.WaitForAssertion(() => Assert.Contains("Haftpflicht", page.Markup, StringComparison.Ordinal));
        Assert.Contains("Privathaftpflicht · HUK", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Haushalt", page.Markup, StringComparison.Ordinal);
        Assert.NotEmpty(page.FindAll("[data-testid=new-object]"));
    }

    [Fact]
    public void TheObjectPageShowsTheDatasheet()
    {
        var page = Render<ObjectPage>(p => p.Add(o => o.Id, PolicyId));

        page.WaitForAssertion(() => Assert.Contains("65,50 €", page.Find("[data-testid=datasheet]").TextContent, StringComparison.Ordinal));
        Assert.Contains("jährlich", page.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Haushalt", page.Find("[data-testid=datasheet-owner]").TextContent.Trim());
        Assert.NotEmpty(page.FindAll("[data-testid=object-edit]"));
        Assert.Contains("aktiv", page.Find("[data-testid=lifecycle-chip]").TextContent, StringComparison.Ordinal);
        Assert.NotEmpty(page.FindAll("[data-testid=lifecycle-change]"));
        Assert.NotEmpty(page.FindAll("[data-testid=object-task-new]"));
    }

    [Fact]
    public void ValidationErrorsAreShownAtTheField()
    {
        _api.OnJson("POST /api/objects", new ObjectValidationProblem(ObjectErrors.Invalid, new() { ["insurer"] = "required" }), HttpStatusCode.BadRequest);
        var page = Render<ObjectPage>(p => p.Add(o => o.TypeKey, "insurance"));

        page.WaitForElement("input[data-testid=object-title]").Change("Neue Police");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Contains("Pflichtfeld", page.Markup, StringComparison.Ordinal));
        Assert.Contains("POST /api/objects", _api.Calls);
    }

    [Fact]
    public void ATypeOnlyKeptInMyLifeIsNotOffered()
    {
        var page = Render<ObjectPage>(p => p.Add(o => o.TypeKey, "funeral"));

        page.WaitForAssertion(() => Assert.Contains("gibt es nicht", page.Markup, StringComparison.Ordinal));
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
