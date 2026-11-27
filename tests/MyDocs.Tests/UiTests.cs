using System.Globalization;
using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MyDocs.ApiClient;
using MyDocs.Shared.Auth;
using MyDocs.Shared.Dashboard;
using MyDocs.Shared.Tasks;
using MyDocs.UI;
using MyDocs.UI.Layout;
using MyDocs.UI.Pages;

namespace MyDocs.Tests;

public sealed class UiTests : BunitContext
{
    private readonly FakeApi _api = new();

    public UiTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMyDocsUi();
        Services.AddMyDocsApi(new Uri("http://localhost/"));
        Services.AddScoped(_ => new HttpClient(_api) { BaseAddress = new Uri("http://localhost/") });
        _api.OnJson("GET /api/setup/status", new SetupStatusDto(false))
            .OnProblem("GET /api/auth/me", "auth-required", HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void HomeShowsTheAreasWithoutThoseOnlyInMyLife()
    {
        var home = Render<Home>();

        Assert.Contains("Willkommen bei myDocs", home.Markup, StringComparison.Ordinal);
        Assert.NotEmpty(home.FindAll("[data-testid=area-4]"));
        Assert.Contains("Versicherungen", home.Markup, StringComparison.Ordinal);
        Assert.Empty(home.FindAll("[data-testid=area-11]"));
        Assert.Empty(home.FindAll("[data-testid=area-12]"));
    }

    [Fact]
    public void HomeShowsTheDashboardAndSwitchesTheScope()
    {
        var task = new TaskDto(Guid.NewGuid(), "review", "Haftpflicht prüfen", null, null, new DateOnly(2026, 11, 1), "normal", "open",
            null, null, null, [], true, DateTimeOffset.UtcNow, "Anna", null, null, 1);
        var objectId = Guid.NewGuid();
        var dashboard = new DashboardDto([task], [], 0, [], [new DeadlineItemDto(new DateOnly(2027, 1, 31), DeadlineKinds.Expiry, objectId, "Personalausweis", "id-card", null)],
            42m, [new PaymentAreaDto(4, 25m, 2)], [new AttentionItemDto(objectId, "Personalausweis", "id-card", ["number"], null)], [], 0) { IncompleteCount = 23 };
        _api.OnJson("GET /api/dashboard", dashboard);

        var home = Render<Home>();

        home.WaitForAssertion(() => Assert.Contains("Haftpflicht prüfen", home.Find("[data-testid=block-due]").TextContent, StringComparison.Ordinal));
        Assert.Contains("Personalausweis", home.Find("[data-testid=block-deadlines]").TextContent, StringComparison.Ordinal);
        Assert.Contains("läuft ab", home.Find("[data-testid=block-deadlines]").TextContent, StringComparison.Ordinal);
        Assert.Contains("42,00 €", home.Find("[data-testid=monthly-total]").TextContent, StringComparison.Ordinal);
        Assert.Contains("23 Einträge mit fehlenden Angaben", home.Find("[data-testid=block-state]").TextContent, StringComparison.Ordinal);

        home.FindAll("[data-testid=scope] .mud-toggle-item")[1].Click();
        home.WaitForAssertion(() => Assert.Equal(2, _api.Calls.Count(c => c == "GET /api/dashboard")));
    }

    [Fact]
    public void HomeKeepsTheAreasWhenTheDashboardFails()
    {
        var home = Render<Home>();

        home.WaitForAssertion(() => Assert.Empty(home.FindAll(".mud-progress-linear")));
        Assert.NotEmpty(home.FindAll("[data-testid=area-4]"));
    }

    [Fact]
    public void LayoutShowsTheLogoAndTheUserMenu()
    {
        AddAuthorization().SetAuthorized("Anna");
        var layout = Render<MainLayout>(p => p.Add(l => l.Body, "<p>Inhalt</p>"));

        var logo = layout.Find("[data-testid=app-logo]");
        Assert.Equal("myDocs", logo.GetAttribute("aria-label"));
        Assert.Contains("Inhalt", layout.Markup, StringComparison.Ordinal);
        Assert.NotEmpty(layout.FindAll("[data-testid=user-menu]"));
    }

    [Fact]
    public void TextsFollowTheLanguage()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");

        var home = Render<Home>();

        Assert.Contains("Welcome to myDocs", home.Markup, StringComparison.Ordinal);
        Assert.Contains("Insurance", home.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void LoginAsksForTheCodeAfterThePassword()
    {
        _api.OnJson("POST /api/auth/login", new LoginResultDto(TwoFactorRequired: true));
        var login = Render<Login>();

        login.Find("input[data-testid=login-email]").Change("admin@example.test");
        login.Find("input[data-testid=login-password]").Change("ein langes Passwort");
        login.Find("form").Submit();

        login.WaitForElement("input[data-testid=login-code]");
        Assert.Contains("POST /api/auth/login", _api.Calls);
    }

    [Fact]
    public void LoginShowsWhyItFailed()
    {
        _api.OnProblem("POST /api/auth/login", AuthErrors.Invalid, HttpStatusCode.Unauthorized);
        var login = Render<Login>();

        login.Find("input[data-testid=login-email]").Change("admin@example.test");
        login.Find("input[data-testid=login-password]").Change("falsch");
        login.Find("form").Submit();

        login.WaitForAssertion(() => Assert.Contains("E-Mail-Adresse oder Passwort stimmen nicht", login.Find("[data-testid=login-error]").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void SetupShowsTheQrCodeAfterTheFirstStep()
    {
        _api.OnJson("GET /api/setup/status", new SetupStatusDto(true))
            .OnJson("POST /api/setup/start", new TotpSetupDto("JBSWY3DPEHPK3PXP", "otpauth://totp/myDocs:a@example.test?secret=JBSWY3DPEHPK3PXP&issuer=myDocs"));
        var setup = Render<Setup>();

        setup.Find("input[data-testid=setup-code]").Change("ABCD-EFGH");
        setup.Find("input[data-testid=setup-name]").Change("Anna");
        setup.Find("input[data-testid=setup-email]").Change("a@example.test");
        setup.Find("input[data-testid=setup-password]").Change("ein langes Passwort");
        setup.Find("form").Submit();

        setup.WaitForAssertion(() => Assert.StartsWith("data:image/svg+xml;base64,", setup.Find("[data-testid=setup-qr]").GetAttribute("src"), StringComparison.Ordinal));
        Assert.Equal("JBSWY3DPEHPK3PXP", setup.Find("[data-testid=setup-secret]").TextContent);
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
