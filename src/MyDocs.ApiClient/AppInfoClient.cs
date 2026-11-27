using System.Net.Http.Json;
using MyDocs.Shared;

namespace MyDocs.ApiClient;

/// <summary>Typisierter Zugriff auf <c>GET /api/info</c>.</summary>
public sealed class AppInfoClient(HttpClient http)
{
    public Task<AppInfoDto?> GetAsync(CancellationToken cancellationToken = default) =>
        http.GetFromJsonAsync<AppInfoDto>("api/info", cancellationToken);
}
