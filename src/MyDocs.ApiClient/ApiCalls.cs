using System.Net.Http.Json;

namespace MyDocs.ApiClient;

/// <summary>Gemeinsame Aufrufe: Fehler der API werden zu <see cref="ApiException"/>.</summary>
internal static class ApiCalls
{
    public static async Task<T> GetAsync<T>(this HttpClient http, string path, CancellationToken ct)
    {
        using var response = await http.GetAsync(new Uri(path, UriKind.Relative), ct);
        await ApiException.EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<T>(ct))!;
    }

    public static async Task<TResponse> SendAsync<TRequest, TResponse>(this HttpClient http, HttpMethod method, string path, TRequest request, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(method, new Uri(path, UriKind.Relative)) { Content = JsonContent.Create(request) };
        using var response = await http.SendAsync(message, ct);
        await ApiException.EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<TResponse>(ct))!;
    }

    public static async Task<TResponse> PostForAsync<TResponse>(this HttpClient http, string path, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative));
        using var response = await http.SendAsync(message, ct);
        await ApiException.EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<TResponse>(ct))!;
    }

    public static async Task SendAsync(this HttpClient http, HttpMethod method, string path, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        using var response = await http.SendAsync(message, ct);
        await ApiException.EnsureSuccessAsync(response, ct);
    }
}
