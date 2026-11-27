using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Files;
using MyDocs.Shared.Documents;
using MyDocs.Shared.Household;
using MyDocs.Shared.Objects;

namespace MyDocs.Tests;

/// <summary>Dokumente: Verschlüsselung, Versionen, Rechte, Vorschau, Zuordnung (M1.5).</summary>
public sealed class DocumentTests(ServerFixture server) : IClassFixture<ServerFixture>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static int _counter;
    private HttpClient _admin = null!;
    private HttpClient _member = null!;
    private HttpClient _guest = null!;
    private Guid _adminPerson;
    private Guid _memberPerson;

    public async ValueTask InitializeAsync()
    {
        var n = Interlocked.Increment(ref _counter);
        _admin = await TestAccounts.AdminAsync(server);
        _member = await TestAccounts.CreateAsync(server, _admin, $"doc-bea{n}@example.test", $"Bea {n}", HouseholdRoles.Member);
        _guest = await TestAccounts.CreateAsync(server, _admin, $"doc-opa{n}@example.test", $"Opa {n}", HouseholdRoles.Guest);
        _adminPerson = (await TestAccounts.MeAsync(_admin)).PersonId!.Value;
        _memberPerson = (await TestAccounts.MeAsync(_member)).PersonId!.Value;
    }

    public ValueTask DisposeAsync()
    {
        _admin.Dispose();
        _member.Dispose();
        _guest.Dispose();
        return ValueTask.CompletedTask;
    }

    private static async Task<Guid> ObjectAsync(HttpClient client, Guid? owner, Guid? responsible, string title = "Haftpflicht")
    {
        var data = new ObjectData { Title = title, Fields = new() { ["insuranceKind"] = "liability", ["insurer"] = "HUK" } };
        var response = await client.PostAsJsonAsync("/api/objects", new ObjectSaveRequest("insurance", data, owner, responsible), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ObjectDto>(Ct))!.Id;
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string path, byte[] content, string fileName, string contentType)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        return await client.PostAsync(new Uri(path, UriKind.Relative), form, Ct);
    }

    private static async Task<DocumentDto> UploadOkAsync(HttpClient client, Guid objectId, byte[] content, string fileName = "Police_2026.pdf", string type = "application/pdf")
    {
        var response = await UploadAsync(client, $"/api/objects/{objectId}/documents", content, fileName, type);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DocumentDto>(Ct))!;
    }

    private static byte[] Pdf(string marker, int size = 2_600_000)
    {
        var bytes = new byte[size];
        RandomNumberGenerator.Fill(bytes);
        var head = Encoding.ASCII.GetBytes($"%PDF-1.7 {marker} ");
        head.CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes(marker).CopyTo(bytes, size - marker.Length);
        return bytes;
    }

    [Fact]
    public async Task UploadedFilesAreEncryptedOnDiskAndComeBackUnchanged()
    {
        var item = await ObjectAsync(_admin, null, _adminPerson);
        var content = Pdf("GEHEIMER-INHALT-" + Guid.NewGuid().ToString("N"));

        var document = await UploadOkAsync(_admin, item, content);

        Assert.Equal(("Police 2026", "application/pdf", content.Length), (document.Title, document.Current.ContentType, (int)document.Current.Size));
        var download = await _guest.GetAsync(new Uri($"/api/documents/{document.Id}/content", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("application/pdf", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("inline", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal(content, await download.Content.ReadAsByteArrayAsync(Ct));

        // Auf der Platte liegt nur Chiffrat, auch kein Stück des Inhalts.
        // Den Ordner für Zwischendateien am Namen erkennen, nicht an „tmp“ irgendwo im Pfad (unter Linux liegt alles in /tmp).
        var files = Directory.GetFiles(Path.Combine(server.DataDirectory, "files"), "*", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(Path.GetDirectoryName(f)) != "tmp").ToList();
        Assert.NotEmpty(files);
        var marker = Encoding.ASCII.GetBytes("GEHEIMER-INHALT-");
        Assert.All(files, f => Assert.Equal(-1, File.ReadAllBytes(f).AsSpan().IndexOf(marker)));
        Assert.Empty(Directory.GetFiles(Path.Combine(server.DataDirectory, "files", "tmp")));
    }

    [Fact]
    public async Task TheSameContentIsStoredOnlyOnce()
    {
        var first = await ObjectAsync(_admin, null, _adminPerson, "Kfz");
        var second = await ObjectAsync(_admin, null, _adminPerson, "Hausrat");
        var content = Pdf("DOPPELT-" + Guid.NewGuid().ToString("N"), 5000);

        await UploadOkAsync(_admin, first, content);
        await UploadOkAsync(_admin, second, content);

        using var scope = server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyDocsDbContext>();
        var versions = await db.DocumentVersions.Where(v => v.Size == 5000).Select(v => v.BlobHash).ToListAsync(Ct);
        var hash = Assert.Single(versions.Distinct());
        Assert.Equal(2, (await db.Blobs.SingleAsync(b => b.Hash == hash, Ct)).RefCount);
    }

    [Fact]
    public async Task NewVersionsKeepTheOldOnes()
    {
        var item = await ObjectAsync(_admin, null, _adminPerson);
        var document = await UploadOkAsync(_admin, item, Encoding.UTF8.GetBytes("Fassung 1"), "Liste.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        var second = await UploadAsync(_member, $"/api/documents/{document.Id}/versions", Encoding.UTF8.GetBytes("Fassung 2"), "Liste.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var updated = (await second.Content.ReadFromJsonAsync<DocumentDto>(Ct))!;

        Assert.Equal([2, 1], updated.Versions.Select(v => v.Number));
        Assert.StartsWith("Bea", updated.Current.CreatedByName, StringComparison.Ordinal);
        Assert.Equal("Fassung 2", await _admin.GetStringAsync(new Uri($"/api/documents/{document.Id}/content", UriKind.Relative), Ct));
        Assert.Equal("Fassung 1", await _admin.GetStringAsync(new Uri($"/api/documents/{document.Id}/content?version=1", UriKind.Relative), Ct));

        // Office-Dateien zeigt der Browser nicht an: Download.
        var office = await _admin.GetAsync(new Uri($"/api/documents/{document.Id}/content", UriKind.Relative), Ct);
        Assert.Equal(("application/octet-stream", "attachment"), (office.Content.Headers.ContentType?.MediaType, office.Content.Headers.ContentDisposition?.DispositionType));
    }

    [Fact]
    public async Task SvgAndHtmlAreNeverShownInline()
    {
        var item = await ObjectAsync(_admin, null, _adminPerson);
        var svg = await UploadOkAsync(_admin, item, Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"), "bild.svg", "image/svg+xml");

        var response = await _admin.GetAsync(new Uri($"/api/documents/{svg.Id}/content", UriKind.Relative), Ct);

        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }

    [Fact]
    public async Task DocumentsInheritTheRightsOfTheirObjects()
    {
        var household = await ObjectAsync(_admin, null, _adminPerson);
        var shared = await UploadOkAsync(_admin, household, Encoding.UTF8.GetBytes("Haushalt"), "a.pdf", "application/pdf");
        var guestUpload = await UploadAsync(_guest, $"/api/objects/{household}/documents", Encoding.UTF8.GetBytes("x"), "x.pdf", "application/pdf");
        Assert.Equal(HttpStatusCode.Forbidden, guestUpload.StatusCode);
        Assert.False((await _guest.GetFromJsonAsync<DocumentDto>($"/api/documents/{shared.Id}", Ct))!.CanEdit);

        var mine = await ObjectAsync(_member, _memberPerson, null, "BU privat");
        var secret = await UploadOkAsync(_member, mine, Encoding.UTF8.GetBytes("privat"), "bu.pdf", "application/pdf");
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync(new Uri($"/api/documents/{secret.Id}", UriKind.Relative), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync(new Uri($"/api/documents/{secret.Id}/content", UriKind.Relative), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync(new Uri($"/api/objects/{mine}/documents", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task ADocumentCanBelongToSeveralObjectsButNeedsOne()
    {
        var account = await ObjectAsync(_admin, null, _adminPerson, "Girokonto");
        var loan = await ObjectAsync(_admin, null, _adminPerson, "Kredit");
        var letter = await UploadOkAsync(_admin, account, Encoding.UTF8.GetBytes("Brief der Bank"), "brief.pdf", "application/pdf");

        var both = await _admin.PutAsJsonAsync($"/api/documents/{letter.Id}/objects", new DocumentObjectsRequest([account, loan]), Ct);
        Assert.Equal(2, (await both.Content.ReadFromJsonAsync<DocumentDto>(Ct))!.ObjectIds.Count);
        Assert.Contains(await _guest.GetFromJsonAsync<List<DocumentDto>>($"/api/objects/{loan}/documents", Ct) ?? [], d => d.Id == letter.Id);

        var none = await _admin.PutAsJsonAsync($"/api/documents/{letter.Id}/objects", new DocumentObjectsRequest([]), Ct);
        Assert.Equal(DocumentErrors.NeedsObject, await SetupTests.TitleAsync(none));

        // Einem fremden privaten Objekt kann niemand ein Dokument unterschieben.
        var foreign = await ObjectAsync(_member, _memberPerson, null, "Privat");
        var push = await _admin.PutAsJsonAsync($"/api/documents/{letter.Id}/objects", new DocumentObjectsRequest([account, foreign]), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, push.StatusCode);
    }

    [Fact]
    public async Task DeletedDocumentsCanBeRestored()
    {
        var item = await ObjectAsync(_admin, null, _adminPerson);
        var document = await UploadOkAsync(_admin, item, Encoding.UTF8.GetBytes("weg"), "weg.pdf", "application/pdf");

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync(new Uri($"/api/documents/{document.Id}", UriKind.Relative), Ct)).StatusCode);
        Assert.DoesNotContain(await _admin.GetFromJsonAsync<List<DocumentDto>>($"/api/objects/{item}/documents", Ct) ?? [], d => d.Id == document.Id);

        Assert.Equal(HttpStatusCode.OK, (await _admin.PostAsync(new Uri($"/api/documents/{document.Id}/restore", UriKind.Relative), null, Ct)).StatusCode);
        Assert.Contains(await _admin.GetFromJsonAsync<List<DocumentDto>>($"/api/objects/{item}/documents", Ct) ?? [], d => d.Id == document.Id);
    }

    [Fact]
    public async Task FilesAboveTheLimitAreRejected()
    {
        using var scope = server.Services.CreateScope();
        var blobs = scope.ServiceProvider.GetRequiredService<BlobStore>();
        using var big = new MemoryStream(new byte[2048]);

        await Assert.ThrowsAsync<FileTooLargeException>(() => blobs.StoreAsync(big, 1024, Ct));

        Assert.Empty(Directory.GetFiles(Path.Combine(server.DataDirectory, "files", "tmp")));
    }

    [Fact]
    public async Task UploadsNeedTheCsrfHeader()
    {
        var item = await ObjectAsync(_admin, null, _adminPerson);
        using var form = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "a.pdf" } };
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/api/objects/{item}/documents", UriKind.Relative)) { Content = form };
        request.Headers.Remove(MyDocs.Shared.ApiHeaders.Csrf);
        using var plain = server.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = true });

        var response = await plain.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
