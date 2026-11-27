using MyDocs.Shared.Search;

namespace MyDocs.ApiClient;

/// <summary>Suche über Objekte und Dokumente.</summary>
public sealed class SearchClient(HttpClient http)
{
    public Task<List<SearchHitDto>> SearchAsync(string query, CancellationToken ct = default) =>
        http.GetAsync<List<SearchHitDto>>($"api/search?q={Uri.EscapeDataString(query)}", ct);
}
