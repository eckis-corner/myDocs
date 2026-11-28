using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MyDocs.Server.Infrastructure;
using MyDocs.Server.Infrastructure.Processing;

namespace MyDocs.Tests;

/// <summary>
/// Gegen den echten Container (LibreOffice, OCRmyPDF, Poppler). Läuft nur mit <c>MYDOCS_WORKER_URL</c>, etwa nach
/// <c>docker run --rm -p 127.0.0.1:18080:8080 --tmpfs /tmp mydocs-worker:dev</c> mit <c>MYDOCS_WORKER_URL=http://localhost:18080</c>.
/// </summary>
public sealed class WorkerIntegrationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HttpDocumentWorker Worker()
    {
        var url = Environment.GetEnvironmentVariable("MYDOCS_WORKER_URL");
        if (string.IsNullOrWhiteSpace(url))
        {
            Assert.Skip("MYDOCS_WORKER_URL ist nicht gesetzt.");
        }

        var services = new ServiceCollection();
        services.AddHttpClient(HttpDocumentWorker.HttpClientName, client => client.Timeout = TimeSpan.FromMinutes(5));
        var provider = services.BuildServiceProvider();
        return new HttpDocumentWorker(provider.GetRequiredService<IHttpClientFactory>(), Options.Create(new MyDocsOptions { WorkerUrl = url }));
    }

    private static WorkerInput Input(byte[] bytes) => new((stream, ct) => stream.WriteAsync(bytes, ct).AsTask(), bytes.Length);

    private static Func<Stream, Task<byte[]>> Bytes => async stream =>
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    };

    [Fact]
    public async Task TextBecomesAPdfWithTextAndAThumbnail()
    {
        var worker = Worker();
        Assert.True(await worker.IsHealthyAsync(Ct));
        var text = Encoding.UTF8.GetBytes("Versicherungsschein Nummer 4711\nHausratversicherung, Beitrag 123,45 Euro\n");

        var pdf = await worker.PdfAsync(Input(text), "office", ".txt", Bytes, Ct);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5), StringComparison.Ordinal);
        Assert.Contains("4711", await worker.TextAsync(Input(pdf), Ct), StringComparison.Ordinal);

        var png = await worker.ThumbnailAsync(Input(pdf), Bytes, Ct);
        Assert.Equal(FakeWorker.PngSignature, png[..8]);

        // Das Vorschaubild als „Scan“: OCR findet den Text wieder.
        var scan = await worker.PdfAsync(Input(png), "image", ".png", Bytes, Ct);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(scan, 0, 5), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WordDocumentsBecomeAPdfPreview()
    {
        var worker = Worker();
        var docx = Docx("Kündigung des Mobilfunkvertrags zum 31.03.2027");

        var pdf = await worker.PdfAsync(Input(docx), "office", ".docx", Bytes, Ct);

        Assert.Contains("Mobilfunkvertrags", await worker.TextAsync(Input(pdf), Ct), StringComparison.Ordinal);
    }

    /// <summary>Kleinste gültige DOCX-Datei mit einem Absatz.</summary>
    private static byte[] Docx(string paragraph)
    {
        using var buffer = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string content)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }

            Add("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                + "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
            Add("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"r1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
            Add("word/document.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">"
                + $"<w:body><w:p><w:r><w:t>{paragraph}</w:t></w:r></w:p></w:body></w:document>");
        }

        return buffer.ToArray();
    }

    [Fact]
    public async Task BrokenFilesAreRejected()
    {
        var worker = Worker();
        await Assert.ThrowsAsync<WorkerRejectedException>(() => worker.PdfAsync(Input("kein PDF"u8.ToArray()), "pdf", ".pdf", Bytes, Ct));
    }
}
