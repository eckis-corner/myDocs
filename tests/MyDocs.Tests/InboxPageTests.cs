using System.Globalization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MyDocs.ApiClient;
using MyDocs.Shared.Admin;
using MyDocs.Shared.Documents;
using MyDocs.Shared.Household;
using MyDocs.UI;
using MyDocs.UI.Components;
using MyDocs.UI.Pages;

namespace MyDocs.Tests;

/// <summary>Seite „Posteingang“ (M3.3) und Stand der Verarbeitung in den Einstellungen (M3.1).</summary>
public sealed class InboxPageTests : BunitContext
{
    private readonly FakeApi _api = new();

    public InboxPageTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMyDocsUi();
        Services.AddMyDocsApi(new Uri("http://localhost/"));
        Services.AddScoped(_ => new HttpClient(_api) { BaseAddress = new Uri("http://localhost/") });
        AddAuthorization().SetAuthorized("Bea").SetRoles(HouseholdRoles.Member);
    }

    private static InboxItemDto Item(string title, bool household, string? previewType, string source = DocumentSources.Scan) =>
        new(Guid.NewGuid(), title, null, null, source, source == DocumentSources.Mail ? "bea@example.test · Rechnung" : null, household, DateTimeOffset.Now,
            new DocumentVersionDto(1, $"{title}.pdf", "application/pdf", 1234, DateTimeOffset.Now, "Bea") { Processing = ProcessingStates.Done, PreviewType = previewType, HasThumbnail = true },
            null);

    [Fact]
    public void ItemsAreListedAndTheFirstIsShownWithItsPreview()
    {
        var scan = Item("Scan vom Montag", household: false, "application/pdf");
        var mail = Item("Rechnung Strom", household: true, null, DocumentSources.Mail);
        _api.OnJson("GET /api/inbox", new List<InboxItemDto> { scan, mail });

        var page = Render<InboxPage>();

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("[data-testid=inbox-item]").Count));
        Assert.Contains("Scanner", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Privat", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Haushalt", page.Markup, StringComparison.Ordinal);
        Assert.Contains($"api/documents/{scan.Id}/preview", page.Find("[data-testid=inbox-preview]").GetAttribute("src"), StringComparison.Ordinal);
        Assert.Equal("Scan vom Montag", page.Find("input[data-testid=inbox-title]").GetAttribute("value"));
        // Ohne Eintrag lässt sich nicht zuordnen.
        Assert.True(page.Find("[data-testid=inbox-assign]").HasAttribute("disabled"));

        page.FindAll("[data-testid=inbox-item]")[1].Click();
        page.WaitForAssertion(() => Assert.Contains("bea@example.test · Rechnung", page.Markup, StringComparison.Ordinal));
        Assert.Empty(page.FindAll("[data-testid=inbox-preview]"));
        Assert.Contains("Keine Vorschau", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void APrivateItemCanBeGivenToTheHousehold()
    {
        var item = Item("Wer kümmert sich?", household: false, null);
        _api.OnJson("GET /api/inbox", new List<InboxItemDto> { item })
            .OnJson($"PUT /api/inbox/{item.Id}/owner", item with { Household = true });

        var page = Render<InboxPage>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid=inbox-to-household]")));
        page.Find("[data-testid=inbox-to-household]").Click();

        page.WaitForAssertion(() => Assert.Contains($"PUT /api/inbox/{item.Id}/owner", _api.Calls));
    }

    [Fact]
    public void AnEmptyInboxSaysSo()
    {
        _api.OnJson("GET /api/inbox", new List<InboxItemDto>());

        var page = Render<InboxPage>();

        page.WaitForAssertion(() => Assert.Contains("Der Posteingang ist leer.", page.Find("[data-testid=inbox-empty]").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void TheCameraPageStartsWithoutPagesAndOpensTheRearCamera()
    {
        var page = Render<CameraPage>();

        Assert.Contains("Noch keine Seiten.", page.Find("[data-testid=camera-empty]").TextContent, StringComparison.Ordinal);
        var input = page.Find("input[data-testid=camera-take-input]");
        Assert.Equal(("image/*", "environment"), (input.GetAttribute("accept"), input.GetAttribute("capture")));
        Assert.Empty(page.FindAll("[data-testid=camera-save]"));
    }

    [Fact]
    public void TheProcessingCardShowsTheWorkerAndOffersARetry()
    {
        _api.OnJson("GET /api/admin/processing", new ProcessingStatusDto(true, false, 3, 2, 40))
            .OnJson("POST /api/admin/processing/retry", 2);

        var card = Render<ProcessingCard>();

        card.WaitForAssertion(() => Assert.Contains("3 warten · 40 fertig · 2 fehlgeschlagen", card.Find("[data-testid=processing-counts]").TextContent, StringComparison.Ordinal));
        Assert.NotEmpty(card.FindAll("[data-testid=processing-down]"));
        card.Find("[data-testid=processing-retry]").Click();
        card.WaitForAssertion(() => Assert.Contains("POST /api/admin/processing/retry", _api.Calls));
    }
}
