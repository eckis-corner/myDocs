using System.Globalization;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using MyDocs.ApiClient;
using MyDocs.Shared;
using MyDocs.Shared.Documents;
using MyDocs.Shared.Household;
using MyDocs.UI;
using MyDocs.UI.Components;

namespace MyDocs.Tests;

public sealed class DocumentPageTests : BunitContext
{
    private static readonly Guid ObjectId = Guid.NewGuid();
    private readonly FakeApi _api = new();

    public DocumentPageTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMyDocsUi();
        Services.AddMyDocsApi(new Uri("http://localhost/"));
        Services.AddScoped(_ => new HttpClient(_api) { BaseAddress = new Uri("http://localhost/") });
        AddAuthorization().SetAuthorized("Anna").SetRoles(HouseholdRoles.Member);
        var now = DateTimeOffset.UtcNow;
        _api.OnJson("GET /api/info", new AppInfoDto("0.1.0", 100))
            .OnJson($"GET /api/objects/{ObjectId}/documents", new List<DocumentDto>
            {
                new(Guid.NewGuid(), "Police 2026", new DateOnly(2026, 1, 15), null,
                    [new(2, "police.pdf", "application/pdf", 250_000, now, "Anna"), new(1, "police.pdf", "application/pdf", 240_000, now.AddDays(-30), "Bea")],
                    [ObjectId], CanEdit: true, now, null),
                new(Guid.NewGuid(), "Haushaltsbuch", null, null, [new(1, "budget.xlsx", "application/vnd.ms-excel", 12_000, now, "Anna")], [ObjectId], CanEdit: true, now, null),
            });
    }

    [Fact]
    public void DocumentsShowTheirVersionsAndSizes()
    {
        var list = Render<DocumentList>(p => p.Add(d => d.ObjectId, ObjectId).Add(d => d.CanEdit, true));

        list.WaitForAssertion(() => Assert.Equal(2, list.FindAll("[data-testid=document-row]").Count));
        Assert.Contains("244,1 KB", list.Markup, StringComparison.Ordinal);
        Assert.Contains("2 Versionen", list.Markup, StringComparison.Ordinal);
        Assert.Contains("15.01.2026", list.Markup, StringComparison.Ordinal);
        // Office-Dateien nur als Download, keine Vorschau.
        Assert.Contains("download=true", list.FindAll("[data-testid=document-row]")[1].InnerHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadersSeeNoUploadButton()
    {
        var list = Render<DocumentList>(p => p.Add(d => d.ObjectId, ObjectId).Add(d => d.CanEdit, false));

        list.WaitForAssertion(() => Assert.Equal(2, list.FindAll("[data-testid=document-row]").Count));
        Assert.Empty(list.FindAll("[data-testid=document-upload]"));
    }

    [Fact]
    public void ChoosingAFileUploadsIt()
    {
        _api.OnJson($"POST /api/objects/{ObjectId}/documents", new DocumentDto(Guid.NewGuid(), "neu", null, null,
            [new(1, "neu.pdf", "application/pdf", 3, DateTimeOffset.UtcNow, "Anna")], [ObjectId], true, DateTimeOffset.UtcNow, null));
        var list = Render<DocumentList>(p => p.Add(d => d.ObjectId, ObjectId).Add(d => d.CanEdit, true));
        list.WaitForAssertion(() => Assert.NotEmpty(list.FindAll("[data-testid=document-row]")));

        list.FindComponents<InputFile>()[0].UploadFiles(InputFileContent.CreateFromText("PDF", "neu.pdf", contentType: "application/pdf"));

        list.WaitForAssertion(() => Assert.Contains($"POST /api/objects/{ObjectId}/documents", _api.Calls));
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
