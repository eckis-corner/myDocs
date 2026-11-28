using System.Net.Http.Headers;
using MyDocs.Shared.Documents;

namespace MyDocs.ApiClient;

/// <summary>Posteingang (M3.3): Dokumente ohne Eintrag zuordnen, weitergeben, verwerfen.</summary>
public sealed class InboxClient(HttpClient http)
{
    public Task<List<InboxItemDto>> ListAsync(bool discarded = false, CancellationToken ct = default) =>
        http.GetAsync<List<InboxItemDto>>($"api/inbox{(discarded ? "?discarded=true" : string.Empty)}", ct);

    public async Task<InboxItemDto> UploadAsync(Stream content, string fileName, string contentType, bool household, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var file = new StreamContent(content);
        file.Headers.ContentType = MediaTypeHeaderValue.TryParse(contentType, out var type) ? type : new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri($"api/inbox{(household ? "?household=true" : string.Empty)}", UriKind.Relative)) { Content = form };
        using var response = await http.SendAsync(message, ct);
        await ApiException.EnsureSuccessAsync(response, ct);
        return (await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<InboxItemDto>(response.Content, ct))!;
    }

    /// <summary>Kamera: die Seiten als JPEG in ihrer Reihenfolge; der Server macht daraus ein PDF.</summary>
    public async Task<InboxItemDto> UploadCameraAsync(IReadOnlyList<byte[]> pages, bool household, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        for (var i = 0; i < pages.Count; i++)
        {
            var page = new ByteArrayContent(pages[i]);
            page.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(page, "page", $"seite-{i + 1}.jpg");
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri($"api/inbox/camera{(household ? "?household=true" : string.Empty)}", UriKind.Relative)) { Content = form };
        using var response = await http.SendAsync(message, ct);
        await ApiException.EnsureSuccessAsync(response, ct);
        return (await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<InboxItemDto>(response.Content, ct))!;
    }

    public Task<DocumentDto> AssignAsync(Guid id, InboxAssignRequest request, CancellationToken ct = default) =>
        http.SendAsync<InboxAssignRequest, DocumentDto>(HttpMethod.Post, $"api/inbox/{id}/assign", request, ct);

    public Task<InboxItemDto> SetHouseholdAsync(Guid id, bool household, CancellationToken ct = default) =>
        http.SendAsync<InboxOwnerRequest, InboxItemDto>(HttpMethod.Put, $"api/inbox/{id}/owner", new InboxOwnerRequest(household), ct);

    public Task DiscardAsync(Guid id, CancellationToken ct = default) => http.SendAsync(HttpMethod.Delete, $"api/documents/{id}", ct);

    public Task RestoreAsync(Guid id, CancellationToken ct = default) => http.SendAsync(HttpMethod.Post, $"api/documents/{id}/restore", ct);
}
