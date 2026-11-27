using System.Net;
using System.Net.Http.Json;
using MyDocs.Shared.Objects;

namespace MyDocs.ApiClient;

/// <summary>Objekte des Katalogs.</summary>
public sealed class ObjectClient(HttpClient http)
{
    public Task<List<ObjectSummaryDto>> ListAsync(int? area = null, string? type = null, CancellationToken ct = default)
    {
        var query = type is not null ? $"?type={Uri.EscapeDataString(type)}" : area is { } a ? $"?area={a}" : string.Empty;
        return http.GetAsync<List<ObjectSummaryDto>>($"api/objects{query}", ct);
    }

    public Task<List<ObjectSummaryDto>> TrashAsync(CancellationToken ct = default) => http.GetAsync<List<ObjectSummaryDto>>("api/objects/trash", ct);

    public Task<ObjectDto> GetAsync(Guid id, CancellationToken ct = default) => http.GetAsync<ObjectDto>($"api/objects/{id}", ct);

    public Task<ObjectDto> CreateAsync(ObjectSaveRequest request, CancellationToken ct = default) => SaveAsync(HttpMethod.Post, "api/objects", request, ct);

    public Task<ObjectDto> UpdateAsync(Guid id, ObjectSaveRequest request, CancellationToken ct = default) => SaveAsync(HttpMethod.Put, $"api/objects/{id}", request, ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default) => http.SendAsync(HttpMethod.Delete, $"api/objects/{id}", ct);

    public Task<ObjectDto> RestoreAsync(Guid id, CancellationToken ct = default) => http.PostForAsync<ObjectDto>($"api/objects/{id}/restore", ct);

    public Task<ObjectDto> SetSharesAsync(Guid id, IReadOnlyList<ObjectShareDto> shares, CancellationToken ct = default) =>
        http.SendAsync<IReadOnlyList<ObjectShareDto>, ObjectDto>(HttpMethod.Put, $"api/objects/{id}/shares", shares, ct);

    public Task<List<ObjectHistoryDto>> HistoryAsync(Guid id, CancellationToken ct = default) => http.GetAsync<List<ObjectHistoryDto>>($"api/objects/{id}/history", ct);

    /// <summary>Speichern; Prüffehler kommen als <see cref="ObjectValidationException"/> mit den Fehlern je Feld.</summary>
    private async Task<ObjectDto> SaveAsync(HttpMethod method, string path, ObjectSaveRequest request, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(request) };
        using var response = await http.SendAsync(message, ct);
        if (response.StatusCode == HttpStatusCode.BadRequest
            && await response.Content.ReadFromJsonAsync<ObjectValidationProblem>(ct) is { Title: ObjectErrors.Invalid, Errors: { } errors })
        {
            throw new ObjectValidationException(errors);
        }

        await ApiException.EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<ObjectDto>(ct))!;
    }
}

/// <summary>Das Objekt ist nicht gültig: Feldschlüssel (oder <c>title</c>, <c>responsible</c>) → Fehlercode.</summary>
public sealed class ObjectValidationException(IReadOnlyDictionary<string, string> errors) : Exception(ObjectErrors.Invalid)
{
    public IReadOnlyDictionary<string, string> Errors { get; } = errors;
}
