using System.Globalization;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MyDocs.ApiClient;
using MyDocs.Shared.Documents;
using MyDocs.Shared.Household;
using MyDocs.Shared.Objects;
using MyDocs.Shared.Payments;
using MyDocs.UI;
using MyDocs.UI.Pages;

namespace MyDocs.Tests;

/// <summary>Seiten „Zahlungen“ und „Aufräumen“ (M6).</summary>
public sealed class ComfortPageTests : BunitContext
{
    private readonly FakeApi _api = new();

    public ComfortPageTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMyDocsUi();
        Services.AddMyDocsApi(new Uri("http://localhost/"));
        Services.AddScoped(_ => new HttpClient(_api) { BaseAddress = new Uri("http://localhost/") });
        AddAuthorization().SetAuthorized("Bea").SetRoles(HouseholdRoles.Member);
        Render<MudBlazor.MudPopoverProvider>();
    }

    [Fact]
    public void PaymentsAddUpAndTheCancelledFilterShowsWhatDrops()
    {
        _api.OnJson("GET /api/payments", new List<PaymentDto>
        {
            new(Guid.NewGuid(), "Hausrat", "insurance", 4, 120, "yearly", null, "Girokonto", null, "Bea", true, null),
            new(Guid.NewGuid(), "Handy", "contract", 5, 20, "monthly", null, null, null, "Bea", false, new DateOnly(2027, 1, 31)),
        });

        var page = Render<PaymentsPage>();

        page.WaitForAssertion(() => Assert.Contains("30,00 €", page.Find("[data-testid=payments-monthly]").TextContent, StringComparison.Ordinal));
        Assert.Contains("10,00 €", page.Find("[data-testid=payments-after-cancellations]").TextContent, StringComparison.Ordinal);
        Assert.Equal(2, page.FindAll("[data-testid=payment-row]").Count);

        page.Find("input[type=checkbox]").Change(true);
        page.WaitForAssertion(() => Assert.Single(page.FindAll("[data-testid=payment-row]")));
        Assert.Contains("20,00 €", page.Find("[data-testid=payments-cancelled-sum]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void CleanupListsExpiredDocumentsAndKeepsThemOnRequest()
    {
        var id = Guid.NewGuid();
        _api.OnJson("GET /api/objects", new List<ObjectSummaryDto>())
            .OnJson("GET /api/persons", new List<MyDocs.Shared.Household.PersonDto>())
            .OnJson("GET /api/cleanup", new List<CleanupItemDto> { new(id, "Handwerkerrechnung", "invoice", new DateOnly(2020, 3, 1), new DateOnly(2022, 12, 31), RetentionBasis.EndOfYear, 2, [], true) })
            .On($"PUT /api/documents/{id}/retention", _ =>
            {
                _api.OnJson("GET /api/cleanup", new List<CleanupItemDto>());
                return new HttpResponseMessage(System.Net.HttpStatusCode.NoContent);
            });

        var page = Render<CleanupPage>();

        page.WaitForAssertion(() => Assert.Contains("2 Jahre ab Jahresende", page.Find("[data-testid=cleanup-row]").TextContent, StringComparison.Ordinal));
        page.Find("[data-testid=cleanup-forever]").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid=cleanup-empty]")));
        Assert.Contains($"PUT /api/documents/{id}/retention", _api.Calls);
    }
}
