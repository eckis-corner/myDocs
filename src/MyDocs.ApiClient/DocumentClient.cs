using System.Net.Http.Headers;
using MyDocs.Shared.Documents;

namespace MyDocs.ApiClient;

/// <summary>Dokumente an Objekten.</summary>
public sealed class DocumentClient(HttpClient http)
{
    public Task<List<DocumentDto>> ListAsync(Guid objectId, CancellationToken ct = default) => http.GetAsync<List<DocumentDto>>($"api/objects/{objectId}/documents", ct);

    public Task<DocumentDto> GetAsync(Guid id, CancellationToken ct = default) => http.GetAsync<DocumentDto>($"api/documents/{id}", ct);

    public Task<DocumentDto> UploadAsync(Guid objectId, Stream content, string fileName, string contentType, CancellationToken ct = default) =>
        PostFileAsync($"api/objects/{objectId}/documents", content, fileName, contentType, ct);

    public Task<DocumentDto> AddVersionAsync(Guid id, Stream content, string fileName, string contentType, CancellationToken ct = default) =>
        PostFileAsync($"api/documents/{id}/versions", content, fileName, contentType, ct);

    public Task<DocumentDto> UpdateAsync(Guid id, DocumentUpdateRequest request, CancellationToken ct = default) =>
        http.SendAsync<DocumentUpdateRequest, DocumentDto>(HttpMethod.Put, $"api/documents/{id}", request, ct);

    public Task<DocumentDto> SetObjectsAsync(Guid id, IReadOnlyList<Guid> objectIds, CancellationToken ct = default) =>
        http.SendAsync<DocumentObjectsRequest, DocumentDto>(HttpMethod.Put, $"api/documents/{id}/objects", new DocumentObjectsRequest(objectIds), ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default) => http.SendAsync(HttpMethod.Delete, $"api/documents/{id}", ct);

    /// <summary>Check-out (Desktop-Client): Sperre setzen und die ausgecheckte Versionsnummer erfahren.</summary>
    public Task<CheckoutDto> CheckOutAsync(Guid id, CancellationToken ct = default) => http.PostForAsync<CheckoutDto>($"api/documents/{id}/checkout", ct);

    public Task RenewCheckoutAsync(Guid id, CancellationToken ct = default) => http.SendAsync(HttpMethod.Put, $"api/documents/{id}/checkout", ct);

    /// <summary>Einchecken bzw. Sperre aufheben (Person mit der Sperre oder Administrator).</summary>
    public Task CheckInAsync(Guid id, CancellationToken ct = default) => http.SendAsync(HttpMethod.Delete, $"api/documents/{id}/checkout", ct);

    public async Task<CheckoutUploadResult> UploadCheckoutAsync(Guid id, int baseVersion, Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var file = new StreamContent(content);
        file.Headers.ContentType = MediaTypeHeaderValue.TryParse(contentType, out var type) ? type : new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri($"api/documents/{id}/checkout/versions?baseVersion={baseVersion}", UriKind.Relative)) { Content = form };
        using var response = await http.SendAsync(message, ct);
        await ApiException.EnsureSuccessAsync(response, ct);
        return (await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<CheckoutUploadResult>(response.Content, ct))!;
    }

    /// <summary>Inhalt einer Version in eine Datei schreiben.</summary>
    public async Task DownloadAsync(Guid id, int? version, string path, CancellationToken ct = default)
    {
        using var response = await http.GetAsync(new Uri(ContentUrl(id, version, download: true), UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, ct);
        await ApiException.EnsureSuccessAsync(response, ct);
        await using var file = File.Create(path);
        await response.Content.CopyToAsync(file, ct);
    }

    /// <summary>Adresse des Inhalts (für Vorschau und Download direkt im Browser, mit dem Sitzungs-Cookie).</summary>
    public static string ContentUrl(Guid id, int? version = null, bool download = false)
    {
        var query = new List<string>();
        if (version is { } v)
        {
            query.Add($"version={v}");
        }

        if (download)
        {
            query.Add("download=true");
        }

        return $"api/documents/{id}/content{(query.Count > 0 ? "?" + string.Join('&', query) : string.Empty)}";
    }

    /// <summary>Vorschau: PDF aus dem Worker oder das Original, wenn der Browser es zeigen darf.</summary>
    public static string PreviewUrl(Guid id, int? version = null) => $"api/documents/{id}/preview{(version is { } v ? $"?version={v}" : string.Empty)}";

    public static string ThumbnailUrl(Guid id, int? version = null) => $"api/documents/{id}/thumbnail{(version is { } v ? $"?version={v}" : string.Empty)}";

    private async Task<DocumentDto> PostFileAsync(string path, Stream content, string fileName, string contentType, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        var file = new StreamContent(content);
        file.Headers.ContentType = MediaTypeHeaderValue.TryParse(contentType, out var type) ? type : new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = form };
        using var response = await http.SendAsync(message, ct);
        await ApiException.EnsureSuccessAsync(response, ct);
        return (await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<DocumentDto>(response.Content, ct))!;
    }
}
