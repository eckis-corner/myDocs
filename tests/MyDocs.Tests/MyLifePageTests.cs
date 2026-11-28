using System.Globalization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MyDocs.ApiClient;
using MyDocs.Shared.Household;
using MyDocs.Shared.MyLife;
using MyDocs.UI;
using MyDocs.UI.Pages.Account;

namespace MyDocs.Tests;

/// <summary>Seite „myLife“ (M5.1): verbinden, Token einmal zeigen, Stand und was übertragen wird.</summary>
public sealed class MyLifePageTests : BunitContext
{
    private readonly FakeApi _api = new();

    public MyLifePageTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMyDocsUi();
        Services.AddMyDocsApi(new Uri("http://localhost/"));
        Services.AddScoped(_ => new HttpClient(_api) { BaseAddress = new Uri("http://localhost/") });
        AddAuthorization().SetAuthorized("Bea").SetRoles(HouseholdRoles.Member);
    }

    private static MyLifeLinkDto Status(bool connected) => new(connected, connected ? DateTimeOffset.Now : null, null, null, connected, null,
        [new MyLifeObjectDto(Guid.NewGuid(), "insurance", "Haftpflicht", 1)], "https://mylife.example.test/", true);

    [Fact]
    public void ConnectingShowsTheTokenOnceAndTheLinkToMyLife()
    {
        _api.OnJson("GET /api/account/mylife", Status(false));
        _api.OnJson("POST /api/account/mylife", new MyLifeConnectResult("mdl_geheim", "https://mydocs.example.test/", "https://mylife.example.test/mydocs#server=x&token=mdl_geheim"));

        var page = Render<MyLifePage>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid=mylife-not-connected]")));
        Assert.Single(page.FindAll("[data-testid=mylife-object]"));
        Assert.Contains("Haftpflicht", page.Markup, StringComparison.Ordinal);

        _api.OnJson("GET /api/account/mylife", Status(true));
        page.Find("[data-testid=mylife-connect]").Click();

        page.WaitForAssertion(() => Assert.Equal("mdl_geheim", page.Find("[data-testid=mylife-token]").GetAttribute("value")));
        Assert.Equal("https://mylife.example.test/mydocs#server=x&token=mdl_geheim", page.Find("[data-testid=mylife-open]").GetAttribute("href"));
        Assert.NotEmpty(page.FindAll("[data-testid=mylife-pending]"));
    }
}
