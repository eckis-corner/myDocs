using System.Globalization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MyDocs.ApiClient;
using MyDocs.Shared.Auth;
using MyDocs.Shared.Household;
using MyDocs.UI;
using MyDocs.UI.Pages;
using MyDocs.UI.Pages.Account;

namespace MyDocs.Tests;

public sealed class PasskeyPageTests : BunitContext
{
    private const string Module = "./_content/MyDocs.UI/js/mydocs.webauthn.js";
    private readonly FakeApi _api = new();

    public PasskeyPageTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMyDocsUi();
        Services.AddMyDocsApi(new Uri("http://localhost/"));
        Services.AddScoped(_ => new HttpClient(_api) { BaseAddress = new Uri("http://localhost/") });
        var module = JSInterop.SetupModule(Module);
        module.Setup<bool>("isSupported").SetResult(true);
        module.Setup<string>("get", _ => true).SetResult("{\"id\":\"abc\"}");
        module.Setup<string>("create", _ => true).SetResult("{\"id\":\"neu\"}");
        _api.OnJson("GET /api/setup/status", new SetupStatusDto(false));
    }

    [Fact]
    public void AfterThePasswordAPasskeyCanReplaceTheCode()
    {
        _api.OnJson("POST /api/auth/login", new LoginResultDto(TwoFactorRequired: true, PasskeyAvailable: true))
            .OnJson("POST /api/auth/login/passkey/options", new PasskeyOptionsDto("{\"challenge\":\"x\"}"))
            .On("POST /api/auth/login/passkey", _ => new HttpResponseMessage(System.Net.HttpStatusCode.NoContent))
            .OnJson("GET /api/auth/me", new MeDto(Guid.NewGuid(), "pia@example.test", "Pia", false, HouseholdRoles.Member, Guid.NewGuid(), 10));
        var login = Render<Login>();
        login.Find("input[data-testid=login-email]").Change("pia@example.test");
        login.Find("input[data-testid=login-password]").Change("ein langes Passwort");
        login.Find("form").Submit();

        login.WaitForElement("[data-testid=login-passkey]").Click();

        login.WaitForAssertion(() => Assert.Contains("POST /api/auth/login/passkey", _api.Calls));
    }

    [Fact]
    public void WithoutPasskeyThereIsNoPasskeyButton()
    {
        _api.OnJson("POST /api/auth/login", new LoginResultDto(TwoFactorRequired: true, PasskeyAvailable: false));
        var login = Render<Login>();
        login.Find("input[data-testid=login-email]").Change("pia@example.test");
        login.Find("input[data-testid=login-password]").Change("ein langes Passwort");
        login.Find("form").Submit();

        login.WaitForElement("input[data-testid=login-code]");
        Assert.Empty(login.FindAll("[data-testid=login-passkey]"));
    }

    [Fact]
    public void TheSecurityPageAddsAPasskey()
    {
        AddAuthorization().SetAuthorized("Pia").SetRoles(HouseholdRoles.Member);
        _api.OnJson("GET /api/auth/me", new MeDto(Guid.NewGuid(), "pia@example.test", "Pia", false, HouseholdRoles.Member, Guid.NewGuid(), 7))
            .OnJson("GET /api/account/passkeys", new List<PasskeyDto>())
            .OnJson("POST /api/account/passkeys/options", new PasskeyOptionsDto("{\"challenge\":\"x\"}"))
            .OnJson("POST /api/account/passkeys", new PasskeyDto("neu", "Laptop", DateTimeOffset.UtcNow));
        var page = Render<SecurityPage>();
        page.WaitForAssertion(() => Assert.Contains("Noch 7 von 10", page.Find("[data-testid=recovery-left]").TextContent, StringComparison.Ordinal));

        page.WaitForElement("button[data-testid=passkey-add]").Click();

        page.WaitForAssertion(() => Assert.Contains("POST /api/account/passkeys", _api.Calls));
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
