using System.Globalization;
using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MyDocs.ApiClient;
using MyDocs.Shared.Admin;
using MyDocs.Shared.Household;
using MyDocs.UI;
using MyDocs.UI.Pages.Admin;

namespace MyDocs.Tests;

public sealed class BackupPageTests : BunitContext
{
    private readonly FakeApi _api = new();

    public BackupPageTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMyDocsUi();
        Services.AddMyDocsApi(new Uri("http://localhost/"));
        Services.AddScoped(_ => new HttpClient(_api) { BaseAddress = new Uri("http://localhost/") });
        AddAuthorization().SetAuthorized("Admin").SetRoles(HouseholdRoles.Member, HouseholdRoles.Admin);
    }

    [Fact]
    public void WithoutBackupKeyThePageSaysSo()
    {
        _api.OnJson("GET /api/admin/backup", new BackupStatusDto(false, null, null, null, null, []));

        var page = Render<BackupPage>();

        page.WaitForAssertion(() => Assert.Contains("MYDOCS_BACKUP_KEY", page.Find("[data-testid=backup-not-configured]").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void TheMasterKeyAppearsAsTwentyFourNumberedWords()
    {
        var now = DateTimeOffset.UtcNow;
        _api.OnJson("GET /api/admin/backup", new BackupStatusDto(true, now, null, null, now.AddDays(1), [new("mydocs-20261127-030000.mdbak", now, 5_000_000)]))
            .OnJson("POST /api/admin/master-key", new MasterKeyDto([.. Enumerable.Range(1, 24).Select(i => $"wort{i}")], now));
        var page = Render<BackupPage>();
        page.WaitForAssertion(() => Assert.Contains("mydocs-20261127-030000.mdbak", page.Markup, StringComparison.Ordinal));
        Assert.Contains("4,8 MB", page.Markup, StringComparison.Ordinal);

        page.Find("input[data-testid=master-key-code]").Change("123456");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Contains("wort24", page.Find("[data-testid=master-key-words]").TextContent, StringComparison.Ordinal));
        Assert.Contains("24.", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AWrongCodeShowsNoWords()
    {
        _api.OnJson("GET /api/admin/backup", new BackupStatusDto(true, null, null, null, null, []))
            .OnProblem("POST /api/admin/master-key", "auth-code-invalid", HttpStatusCode.Forbidden);
        var page = Render<BackupPage>();
        page.WaitForElement("input[data-testid=master-key-code]").Change("000000");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Contains("POST /api/admin/master-key", _api.Calls));
        Assert.Empty(page.FindAll("[data-testid=master-key-words]"));
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
