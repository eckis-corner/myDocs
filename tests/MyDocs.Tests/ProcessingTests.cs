using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure;
using MyDocs.Server.Infrastructure.Files;
using MyDocs.Server.Infrastructure.Processing;
using MyDocs.Shared.Admin;
using MyDocs.Shared.Documents;
using MyDocs.Shared.Objects;

namespace MyDocs.Tests;

/// <summary>Verarbeitung im Worker (M3.1): OCR, Office-Vorschau, Vorschaubild, Text, Fehler und Wiederholung.</summary>
public sealed class ProcessingTests(ServerFixture server) : IClassFixture<ServerFixture>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private HttpClient _admin = null!;
    private Guid _person;

    public async ValueTask InitializeAsync()
    {
        _admin = await TestAccounts.AdminAsync(server);
        _person = (await TestAccounts.MeAsync(_admin)).PersonId!.Value;
    }

    public ValueTask DisposeAsync()
    {
        _admin.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<Guid> ObjectAsync()
    {
        var data = new ObjectData { Title = "Hausrat", Fields = new() { ["insuranceKind"] = "liability", ["insurer"] = "HUK" } };
        var response = await _admin.PostAsJsonAsync("/api/objects", new ObjectSaveRequest("insurance", data, null, _person), Ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<ObjectDto>(Ct))!.Id;
    }

    private async Task<DocumentDto> UploadAsync(Guid objectId, string content, string fileName, string type = "application/octet-stream")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        file.Headers.ContentType = new MediaTypeHeaderValue(type);
        form.Add(file, "file", fileName);
        var response = await _admin.PostAsync(new Uri($"/api/objects/{objectId}/documents", UriKind.Relative), form, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DocumentDto>(Ct))!;
    }

    private Task<int> ProcessAsync() => server.Services.GetRequiredService<ProcessingService>().RunOnceAsync(Ct);

    private async Task<DocumentDto> GetAsync(Guid id) => (await _admin.GetFromJsonAsync<DocumentDto>($"/api/documents/{id}", Ct))!;

    private async Task<T> WithDbAsync<T>(Func<MyDocsDbContext, Task<T>> query)
    {
        await using var scope = server.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<MyDocsDbContext>());
    }

    private Task<ProcessingJob> JobAsync(Guid documentId) => WithDbAsync(db =>
        db.ProcessingJobs.AsNoTracking().FirstAsync(j => j.SourceHash == db.DocumentVersions.Where(v => v.DocumentId == documentId).OrderByDescending(v => v.Number).Select(v => v.BlobHash).First(), Ct));

    [Fact]
    public async Task ScannedPdfGetsATextLayerAThumbnailAndItsTextIsStored()
    {
        var document = await UploadAsync(await ObjectAsync(), "%PDF-1.4 SCAN Versicherungsschein Nummer HR-4711-99", "Scan.pdf", "application/pdf");
        Assert.Equal(ProcessingStates.Pending, document.Current.Processing);
        Assert.False(document.Current.HasThumbnail);

        await ProcessAsync();

        var processed = await GetAsync(document.Id);
        Assert.Equal(ProcessingStates.Done, processed.Current.Processing);
        Assert.Equal("application/pdf", processed.Current.PreviewType);
        Assert.True(processed.Current.HasThumbnail);

        // Vorschau ist das PDF mit Textebene, der Download bleibt das Original (24).
        var preview = await _admin.GetByteArrayAsync($"/api/documents/{document.Id}/preview", Ct);
        Assert.StartsWith(FakeWorker.OcrMarker, Encoding.UTF8.GetString(preview), StringComparison.Ordinal);
        var original = await _admin.GetStringAsync($"/api/documents/{document.Id}/content?download=true", Ct);
        Assert.StartsWith("%PDF-1.4 SCAN", original, StringComparison.Ordinal);
        using var thumbnail = await _admin.GetAsync($"/api/documents/{document.Id}/thumbnail", Ct);
        Assert.Equal("image/png", thumbnail.Content.Headers.ContentType?.MediaType);
        Assert.Equal(FakeWorker.PngSignature, (await thumbnail.Content.ReadAsByteArrayAsync(Ct))[..8]);

        var text = await WithDbAsync(db => db.Documents.Where(d => d.Id == document.Id).Select(d => d.ContentText).FirstAsync(Ct));
        Assert.Contains("HR-4711-99", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\f", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PdfWithTextIsNotOcredAgain()
    {
        server.Worker.Calls.Clear();
        var document = await UploadAsync(await ObjectAsync(), "%PDF-1.7 Kündigungsbestätigung zum Jahresende, Vertragsnummer KB-1234, mit freundlichen Grüßen", "Brief.pdf", "application/pdf");

        await ProcessAsync();

        Assert.DoesNotContain(server.Worker.Calls, c => c.StartsWith("pdf:", StringComparison.Ordinal));
        var processed = await GetAsync(document.Id);
        Assert.Equal("application/pdf", processed.Current.PreviewType);
        Assert.True(processed.Current.HasThumbnail);
        // Ohne eigene Ableitung zeigt die Vorschau das Original.
        Assert.StartsWith("%PDF-1.7 Kündigung", await _admin.GetStringAsync($"/api/documents/{document.Id}/preview", Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OfficeFilesGetAPdfPreview()
    {
        server.Worker.Calls.Clear();
        var document = await UploadAsync(await ObjectAsync(), "PK\u0003\u0004 Haushaltsliste 2027 Strom Gas Wasser", "Liste.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        Assert.Null(document.Current.PreviewType);

        await ProcessAsync();

        Assert.Contains("pdf:office:.xlsx", server.Worker.Calls);
        var processed = await GetAsync(document.Id);
        Assert.Equal("application/pdf", processed.Current.PreviewType);
        using var preview = await _admin.GetAsync($"/api/documents/{document.Id}/preview", Ct);
        Assert.Equal("application/pdf", preview.Content.Headers.ContentType?.MediaType);
        Assert.Equal("inline", preview.Content.Headers.ContentDisposition?.DispositionType);
        // Der Download bleibt das Original und wird nie im Browser angezeigt.
        using var download = await _admin.GetAsync($"/api/documents/{document.Id}/content", Ct);
        Assert.Equal("application/octet-stream", download.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ARejectedStepFinishesTheJobWithTheError()
    {
        var document = await UploadAsync(await ObjectAsync(), "PK\u0003\u0004 REJECT", "Kaputt.docx");

        await ProcessAsync();

        var job = await JobAsync(document.Id);
        Assert.Equal(ProcessingStates.Done, job.State);
        Assert.Equal(FileKind.Office, job.Kind);
        Assert.Contains("kaputt", job.Error, StringComparison.Ordinal);
        var processed = await GetAsync(document.Id);
        Assert.Null(processed.Current.PreviewType);
        Assert.False(processed.Current.HasThumbnail);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"/api/documents/{document.Id}/preview", Ct)).StatusCode);
    }

    [Fact]
    public async Task UnknownFilesAreStoredWithoutProcessing()
    {
        server.Worker.Calls.Clear();
        var document = await UploadAsync(await ObjectAsync(), "nur ein paar Bytes", "Daten.bin");

        await ProcessAsync();

        Assert.Empty(server.Worker.Calls);
        var job = await JobAsync(document.Id);
        Assert.Equal((ProcessingStates.Done, FileKind.Other), (job.State, job.Kind));
    }

    [Fact]
    public async Task SameContentIsProcessedOnce()
    {
        const string content = "%PDF-1.4 SCAN dieselbe Rechnung zweimal hochgeladen";
        var first = await UploadAsync(await ObjectAsync(), content, "Rechnung.pdf", "application/pdf");
        await ProcessAsync();
        server.Worker.Calls.Clear();

        var second = await UploadAsync(await ObjectAsync(), content, "Rechnung-Kopie.pdf", "application/pdf");
        await ProcessAsync();

        Assert.Empty(server.Worker.Calls);
        var processed = await GetAsync(second.Id);
        Assert.Equal(ProcessingStates.Done, processed.Current.Processing);
        Assert.True(processed.Current.HasThumbnail);
        // Der Text steht sofort beim neuen Dokument, ohne neuen Durchlauf.
        var text = await WithDbAsync(db => db.Documents.Where(d => d.Id == second.Id).Select(d => d.ContentText).FirstAsync(Ct));
        Assert.Contains("Rechnung zweimal", text, StringComparison.Ordinal);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task AnUnreachableWorkerIsRetriedWithBackoffThenGivenUpAndCanBeRetried()
    {
        server.Worker.Unavailable = true;
        try
        {
            var document = await UploadAsync(await ObjectAsync(), "%PDF-1.4 SCAN Worker weg", "Weg.pdf", "application/pdf");
            await ProcessAsync();
            var job = await JobAsync(document.Id);
            Assert.Equal((ProcessingStates.Pending, 1), (job.State, job.Attempts));
            Assert.Equal(server.Clock.GetUtcNow().AddMinutes(1), job.NextAttemptAt);

            for (var attempt = 2; attempt <= DocumentProcessor.MaxAttempts; attempt++)
            {
                server.Clock.Advance(TimeSpan.FromDays(1));
                await ProcessAsync();
            }

            // Die Sitzung ist nach Tagen abgelaufen.
            _admin.Dispose();
            _admin = await TestAccounts.AdminAsync(server);
            job = await JobAsync(document.Id);
            Assert.Equal((ProcessingStates.Failed, DocumentProcessor.MaxAttempts), (job.State, job.Attempts));
            Assert.Equal(ProcessingStates.Failed, (await GetAsync(document.Id)).Current.Processing);
            var status = (await _admin.GetFromJsonAsync<ProcessingStatusDto>("/api/admin/processing", Ct))!;
            Assert.True(status.Configured);
            Assert.False(status.Reachable);
            Assert.True(status.Failed >= 1);

            server.Worker.Unavailable = false;
            var retry = await _admin.PostAsync(new Uri("/api/admin/processing/retry", UriKind.Relative), null, Ct);
            Assert.True(await retry.Content.ReadFromJsonAsync<int>(Ct) >= 1);
            await ProcessAsync();

            Assert.Equal(ProcessingStates.Done, (await JobAsync(document.Id)).State);
            Assert.True((await GetAsync(document.Id)).Current.HasThumbnail);
        }
        finally
        {
            server.Worker.Unavailable = false;
        }
    }

    [Fact]
    public async Task OnlyAdministratorsSeeTheProcessingStatus()
    {
        using var member = await TestAccounts.CreateAsync(server, _admin, $"proc-{Guid.NewGuid():N}@example.test", "Bea", MyDocs.Shared.Household.HouseholdRoles.Member);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/admin/processing", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsync(new Uri("/api/admin/processing/retry", UriKind.Relative), null, Ct)).StatusCode);
    }

    [Fact]
    public async Task EmptyingTheTrashRemovesDerivativesAndTheirFiles()
    {
        var document = await UploadAsync(await ObjectAsync(), "%PDF-1.4 SCAN Altes Schreiben zum Wegwerfen", "Alt.pdf", "application/pdf");
        await ProcessAsync();
        var hashes = await WithDbAsync(db => db.BlobDerivatives.Where(d => d.SourceHash == db.DocumentVersions.Where(v => v.DocumentId == document.Id).Select(v => v.BlobHash).First())
            .Select(d => d.BlobHash).ToListAsync(Ct));
        Assert.Equal(2, hashes.Count);
        var blobs = server.Services.CreateScope().ServiceProvider.GetRequiredService<BlobStore>();
        Assert.All(hashes, h => Assert.True(File.Exists(blobs.PathOf(h))));

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/documents/{document.Id}", Ct)).StatusCode);
        server.Clock.Advance(TrashPurge.RetentionPeriod + TimeSpan.FromDays(1));
        await server.Services.GetRequiredService<TrashPurge>().PurgeAsync(Ct);

        Assert.False(await WithDbAsync(db => db.Blobs.AnyAsync(b => hashes.Contains(b.Hash), Ct)));
        Assert.All(hashes, h => Assert.False(File.Exists(blobs.PathOf(h))));
    }

    [Fact]
    public async Task RecognizedTextIsSearchableWithStemsAndShowsWhereItWasFound()
    {
        var document = await UploadAsync(await ObjectAsync(), "%PDF-1.4 SCAN Sehr geehrte Damen und Herren,\nanbei die Beitragsrechnung für Ihre Glasversicherung, Policennummer GV-7788-12.\nMit freundlichen Grüßen",
            "Scan_0001.pdf", "application/pdf");
        await ProcessAsync();

        var exact = (await _admin.GetFromJsonAsync<List<MyDocs.Shared.Search.SearchHitDto>>("/api/search?q=GV-7788", Ct))!;
        var hit = Assert.Single(exact, h => h.Id == document.Id);
        Assert.Contains("GV-7788-12", hit.Snippet, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", hit.Snippet, StringComparison.Ordinal);
        // Wortstamm: „Glasversicherungen“ findet „Glasversicherung“.
        var stem = (await _admin.GetFromJsonAsync<List<MyDocs.Shared.Search.SearchHitDto>>("/api/search?q=Glasversicherungen", Ct))!;
        Assert.Contains(stem, h => h.Id == document.Id && h.Snippet!.Contains("Glasversicherung", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RecognizedTextOfForeignPrivateEntriesIsNotFound()
    {
        using var member = await TestAccounts.CreateAsync(server, _admin, $"proc-{Guid.NewGuid():N}@example.test", "Bea", MyDocs.Shared.Household.HouseholdRoles.Member);
        var person = (await TestAccounts.MeAsync(member)).PersonId!.Value;
        var data = new ObjectData { Title = "Privat", Fields = new() { ["insuranceKind"] = "liability", ["insurer"] = "HUK" } };
        var created = await member.PostAsJsonAsync("/api/objects", new ObjectSaveRequest("insurance", data, person, person), Ct);
        var item = (await created.Content.ReadFromJsonAsync<ObjectDto>(Ct))!.Id;
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent("%PDF-1.4 SCAN Arztbrief Diagnose Zahnwurzel XQ-31"u8.ToArray()), "file", "Arzt.pdf");
        Assert.Equal(HttpStatusCode.OK, (await member.PostAsync(new Uri($"/api/objects/{item}/documents", UriKind.Relative), form, Ct)).StatusCode);

        await ProcessAsync();

        Assert.NotEmpty((await member.GetFromJsonAsync<List<MyDocs.Shared.Search.SearchHitDto>>("/api/search?q=Zahnwurzel", Ct))!);
        Assert.Empty((await _admin.GetFromJsonAsync<List<MyDocs.Shared.Search.SearchHitDto>>("/api/search?q=Zahnwurzel", Ct))!);
    }

    [Theory]
    [InlineData("%PDF-1.7", "x.bin", FileKind.Pdf)]
    [InlineData("ÿØÿà", "foto.jpg", FileKind.Image)]
    [InlineData("PK\u0003\u0004", "liste.xlsx", FileKind.Office)]
    [InlineData("PK\u0003\u0004", "archiv.zip", FileKind.Other)]
    [InlineData("{\\rtf1", "brief.dat", FileKind.Office)]
    [InlineData("Name;Betrag", "konten.csv", FileKind.Text)]
    [InlineData("<svg/>", "bild.svg", FileKind.Other)]
    [InlineData("<html>", "seite.html", FileKind.Other)]
    public void FileKindsAreDetectedFromTheContent(string header, string fileName, string kind)
    {
        var bytes = header.Select(c => (byte)c).ToArray();
        Assert.Equal(kind, FileKinds.Detect(bytes, fileName));
    }

    [Fact]
    public void TextIsNormalizedAndScansWithoutTextAreRecognized()
    {
        Assert.False(DocumentProcessor.HasText("\f\f"));
        Assert.False(DocumentProcessor.HasText("Seite 1\f Seite 2\f"));
        Assert.True(DocumentProcessor.HasText(new string('a', DocumentProcessor.MinLettersPerPage * 2) + "\f\f"));
        Assert.Equal("Zeile 1\n\nZeile 2", DocumentProcessor.Normalize("\n\nZeile 1  \f\n\n\nZeile\0 2\f"));
        Assert.True(DocumentProcessor.Normalize(new string('x', 1000) + "\n" + string.Concat(Enumerable.Repeat("wort ", 100_000))).Length <= DocumentProcessor.MaxTextLength);
    }
}
