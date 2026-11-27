using System.Net;
using System.Net.Http.Json;

namespace MyDocs.ApiClient;

/// <summary>Fehler der API mit dem Code aus dem Problem-Titel (etwa <c>auth-invalid</c>), den die Oberfläche übersetzt.</summary>
public sealed class ApiException(HttpStatusCode status, string code, string? detail = null) : Exception(code)
{
    /// <summary>Zusatz des Servers, etwa die Fehlermeldung des SMTP-Servers beim Test.</summary>
    public string? Detail { get; } = detail;

    public HttpStatusCode Status { get; } = status;

    public string Code { get; } = code;

    internal static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string? code = null;
        string? detail = null;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<Problem>(cancellationToken);
            code = problem?.Title;
            detail = problem?.Detail;
        }
        catch (System.Text.Json.JsonException)
        {
        }
        catch (NotSupportedException)
        {
        }

        throw new ApiException(response.StatusCode, code ?? $"http-{(int)response.StatusCode}", detail);
    }

    private sealed record Problem(string? Title, string? Detail);
}
