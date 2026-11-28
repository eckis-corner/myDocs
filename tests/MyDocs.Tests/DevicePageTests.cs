using System.Globalization;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MyDocs.ApiClient;
using MyDocs.Shared.Devices;
using MyDocs.Shared.Household;
using MyDocs.UI;
using MyDocs.UI.Pages;
using MyDocs.UI.Pages.Account;

namespace MyDocs.Tests;

/// <summary>Seiten „Gerät anmelden“ und „Geräte“ (M4.1).</summary>
public sealed class DevicePageTests : BunitContext
{
    private readonly FakeApi _api = new();

    public DevicePageTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMyDocsUi();
        Services.AddMyDocsApi(new Uri("http://localhost/"));
        Services.AddScoped(_ => new HttpClient(_api) { BaseAddress = new Uri("http://localhost/") });
        AddAuthorization().SetAuthorized("Bea").SetRoles(HouseholdRoles.Member);
    }

    private const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

    [Fact]
    public void AllowingSendsTheCodeToTheLoopbackAddress()
    {
        _api.OnJson("POST /api/devices/authorize", new DeviceAuthorizeResult("http://127.0.0.1:5000/callback?code=abc&state=xyz"));
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"device/authorize?redirect_uri={Uri.EscapeDataString("http://127.0.0.1:5000/callback")}&code_challenge={Challenge}&code_challenge_method=S256&state=xyz&device_name=Laptop");

        var page = Render<DeviceAuthorizePage>();
        Assert.Contains("„Laptop“", page.Markup, StringComparison.Ordinal);
        page.Find("[data-testid=device-allow]").Click();

        page.WaitForAssertion(() => Assert.Equal("http://127.0.0.1:5000/callback?code=abc&state=xyz", navigation.Uri));
    }

    [Theory]
    [InlineData("https://evil.example/callback")]
    [InlineData("http://127.0.0.1/callback")]
    public void ForeignAddressesAreRefusedInTheBrowserToo(string redirect)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo($"device/authorize?redirect_uri={Uri.EscapeDataString(redirect)}&code_challenge={Challenge}&code_challenge_method=S256&device_name=X");

        var page = Render<DeviceAuthorizePage>();

        Assert.NotEmpty(page.FindAll("[data-testid=device-invalid]"));
        Assert.Empty(page.FindAll("[data-testid=device-allow]"));
    }

    [Fact]
    public void DevicesAreListedWithTheCurrentOne()
    {
        _api.OnJson("GET /api/devices", new List<DeviceDto>
        {
            new(Guid.NewGuid(), "Laptop (macOS)", DateTimeOffset.Now, DateTimeOffset.Now, "192.168.1.0", true),
            new(Guid.NewGuid(), "Büro (Windows)", DateTimeOffset.Now, DateTimeOffset.Now, null, false),
        });

        var page = Render<DevicesPage>();

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("[data-testid=device-row]").Count));
        Assert.Contains("Dieses Gerät", page.Markup, StringComparison.Ordinal);
    }
}
