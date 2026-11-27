using System.Globalization;
using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MyDocs.ApiClient;
using MyDocs.Shared.Admin;
using MyDocs.Shared.Auth;
using MyDocs.Shared.Household;
using MyDocs.UI;
using MyDocs.UI.Pages;
using MyDocs.UI.Pages.Admin;

namespace MyDocs.Tests;

public sealed class HouseholdPageTests : BunitContext
{
    private readonly FakeApi _api = new();

    public HouseholdPageTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMyDocsUi();
        Services.AddMyDocsApi(new Uri("http://localhost/"));
        Services.AddScoped(_ => new HttpClient(_api) { BaseAddress = new Uri("http://localhost/") });
        _api.OnJson("GET /api/persons", new List<PersonDto>
        {
            new(Guid.NewGuid(), "Anna", null, Guid.NewGuid(), null),
            new(Guid.NewGuid(), "Ben", new DateOnly(2015, 3, 1), null, "Sohn"),
        });
    }

    [Fact]
    public void MembersSeeTheButtonsToChangePersons()
    {
        AddAuthorization().SetAuthorized("Anna").SetRoles(HouseholdRoles.Member);

        var page = Render<Household>();

        page.WaitForAssertion(() => Assert.Contains("Ben", page.Markup, StringComparison.Ordinal));
        Assert.NotEmpty(page.FindAll("[data-testid=person-create]"));
        Assert.Contains("01.03.2015", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void GuestsOnlyRead()
    {
        AddAuthorization().SetAuthorized("Oma").SetRoles(HouseholdRoles.Guest);

        var page = Render<Household>();

        page.WaitForAssertion(() => Assert.Contains("Ben", page.Markup, StringComparison.Ordinal));
        Assert.Empty(page.FindAll("[data-testid=person-create]"));
    }

    [Fact]
    public void TheUserListShowsRoleAndState()
    {
        AddAuthorization().SetAuthorized("Admin").SetRoles(HouseholdRoles.Member, HouseholdRoles.Admin);
        _api.OnJson("GET /api/admin/users", new List<AdminUserDto>
        {
            new(Guid.NewGuid(), "admin@example.test", "Admin", HouseholdRoles.Member, true, UserStates.Active, DateTimeOffset.UtcNow, null),
            new(Guid.NewGuid(), "oma@example.test", "Oma", HouseholdRoles.Guest, false, UserStates.Pending, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7)),
        });

        var page = Render<Users>();

        page.WaitForAssertion(() => Assert.Contains("oma@example.test", page.Markup, StringComparison.Ordinal));
        Assert.Contains("Mitglied · Administrator", page.Markup, StringComparison.Ordinal);
        Assert.Contains("wartet auf Aktivierung", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivationShowsWhoTheAccountIsFor()
    {
        JSInterop.Setup<string?>("myDocs.getFragment").SetResult("token-123");
        _api.OnJson("POST /api/activation/info", new ActivationInfoDto("anna@example.test", "Anna"));

        var page = Render<Activate>();

        page.WaitForAssertion(() => Assert.Contains("Hallo Anna", page.Markup, StringComparison.Ordinal));
        Assert.NotEmpty(page.FindAll("input[data-testid=activate-password]"));
    }

    [Fact]
    public void AnInvalidActivationLinkSaysSo()
    {
        JSInterop.Setup<string?>("myDocs.getFragment").SetResult("abgelaufen");
        _api.OnProblem("POST /api/activation/info", AuthErrors.ActivationInvalid, HttpStatusCode.NotFound);

        var page = Render<Activate>();

        page.WaitForAssertion(() => Assert.Contains("ungültig, abgelaufen oder schon benutzt", page.Find("[data-testid=activate-error]").TextContent, StringComparison.Ordinal));
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
