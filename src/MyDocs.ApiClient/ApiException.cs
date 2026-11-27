using System.Net;
using System.Net.Http.Json;

namespace MyDocs.ApiClient;

/// <summary>Fehler der API mit dem Code aus dem Problem-Titel (etwa <c>auth-invalid</c>), den die Oberfläche übersetzt.</summary>
public sealed class ApiException(HttpStatusCode status, string code) : Exception(code)
{
    public HttpStatusCode Status { get; } = status;

    public string Code { get; } = code;

    internal static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? code = null;
        try
        {
            code = (await response.Content.ReadFromJsonAsync<Problem>(cancellationToken))?.Title;
        }
        catch (System.Text.Json.JsonException)
        {
        }
        catch (NotSupportedException)
        {
        }

        throw new ApiException(response.StatusCode, code ?? $"http-{(int)response.StatusCode}");
    }

    private sealed record Problem(string? Title);
}
