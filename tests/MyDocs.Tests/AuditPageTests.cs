using System.Globalization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MyDocs.ApiClient;
using MyDocs.Shared.Audit;
using MyDocs.Shared.Household;
using MyDocs.UI;
using MyDocs.UI.Layout;
using MyDocs.UI.Pages.Account;
using MyDocs.UI.Services;

namespace MyDocs.Tests;

public sealed class AuditPageTests : BunitContext
{
    private readonly FakeApi _api = new();

    public AuditPageTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMyDocsUi();
        Services.AddMyDocsApi(new Uri("http://localhost/"));
        Services.AddScoped(_ => new HttpClient(_api) { BaseAddress = new Uri("http://localhost/") });
    }

    [Fact]
    public void TheLogShowsAdminViewsPlainly()
    {
        AddAuthorization().SetAuthorized("Bea").SetRoles(HouseholdRoles.Member);
        _api.OnJson("GET /api/audit", new List<AuditEntryDto>
        {
            new(2, DateTimeOffset.UtcNow, "Admin", AuditActions.AdminView, Guid.NewGuid(), null, "BU Bea", null, null),
            new(1, DateTimeOffset.UtcNow.AddMinutes(-5), "Bea", AuditActions.ObjectCreated, Guid.NewGuid(), null, "BU Bea", null, null),
        });

        var page = Render<AuditPage>();

        page.WaitForAssertion(() => Assert.Contains("Admin-Einsicht", page.Markup, StringComparison.Ordinal));
        Assert.Contains("Eintrag angelegt", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBannerShowsWhileTheAdminViewIsOn()
    {
        AddAuthorization().SetAuthorized("Admin").SetRoles(HouseholdRoles.Member, HouseholdRoles.Admin);
        var layout = Render<MainLayout>(p => p.Add(l => l.Body, "<p>Inhalt</p>"));
        Assert.Empty(layout.FindAll("[data-testid=admin-view-banner]"));

        Services.GetRequiredService<AdminViewService>().Set(true);

        layout.WaitForAssertion(() => Assert.NotEmpty(layout.FindAll("[data-testid=admin-view-banner]")));
        Assert.Equal(AdminViewHeader.Value, Services.GetRequiredService<HttpClient>().DefaultRequestHeaders.GetValues(AdminViewHeader.Name).Single());
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
