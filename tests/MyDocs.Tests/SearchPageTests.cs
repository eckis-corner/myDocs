using System.Globalization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MyDocs.ApiClient;
using MyDocs.Shared.Household;
using MyDocs.Shared.Search;
using MyDocs.UI;
using MyDocs.UI.Pages;

namespace MyDocs.Tests;

public sealed class SearchPageTests : BunitContext
{
    private readonly FakeApi _api = new();

    public SearchPageTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMyDocsUi();
        Services.AddMyDocsApi(new Uri("http://localhost/"));
        Services.AddScoped(_ => new HttpClient(_api) { BaseAddress = new Uri("http://localhost/") });
        AddAuthorization().SetAuthorized("Anna").SetRoles(HouseholdRoles.Member);
    }

    [Fact]
    public void HitsLinkToTheEntryAlsoForDocuments()
    {
        var entry = Guid.NewGuid();
        _api.OnJson("GET /api/search", new List<SearchHitDto>
        {
            new(SearchKinds.Entry, entry, "Haftpflicht", "insurance", null, null, "HUK"),
            new(SearchKinds.Document, Guid.NewGuid(), "Police 2026", null, entry, "Haftpflicht", null),
        });
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("search?q=haft");

        var page = Render<SearchPage>();

        page.WaitForAssertion(() => Assert.Contains("2 Treffer", page.Markup, StringComparison.Ordinal));
        Assert.Contains("Versicherung", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Dokument bei Haftpflicht", page.Markup, StringComparison.Ordinal);
        Assert.Equal(2, page.FindAll($"a[href='object/{entry}']").Count);
    }

    [Fact]
    public void WithoutHitsItSaysSo()
    {
        _api.OnJson("GET /api/search", new List<SearchHitDto>());
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("search?q=nichts");

        var page = Render<SearchPage>();

        page.WaitForAssertion(() => Assert.Contains("Keine Treffer für „nichts“", page.Find("[data-testid=search-empty]").TextContent, StringComparison.Ordinal));
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
