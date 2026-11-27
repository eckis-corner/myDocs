using System.Net.Http.Headers;
using System.Net.Http.Json;
using MyDocs.Server.Infrastructure.Settings;

namespace MyDocs.Server.Infrastructure.Mail;

/// <summary>Push-Nachricht an ein Thema bei ntfy. In den Tests ersetzt durch eine Attrappe.</summary>
public interface IPushSender
{
    /// <summary>Verschickt sofort; wirft, wenn ntfy nicht eingerichtet ist oder ablehnt.</summary>
    Task SendAsync(string topic, string title, string message, string? click, CancellationToken ct);
}

/// <summary>
/// Veröffentlicht als JSON an die Wurzel des ntfy-Servers (Umlaute in Titel und Text). Push-Nachrichten enthalten nur
/// Zahlen, keine Titel: Themen bei ntfy sind für jeden lesbar, der ihren Namen kennt.
/// </summary>
public sealed class NtfyPushSender(IHttpClientFactory clients, SettingsStore settings) : IPushSender
{
    public const string HttpClientName = "ntfy";
    private static readonly string[] Tags = ["page_facing_up"];

    public async Task SendAsync(string topic, string title, string message, string? click, CancellationToken ct)
    {
        var stored = await settings.GetAsync(ct);
        if (stored.Ntfy.ServerUrl is not { } server)
        {
            throw new InvalidOperationException("ntfy ist nicht eingerichtet.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, server + "/")
        {
            Content = click is null
                ? JsonContent.Create(new { topic, title, message, tags = Tags })
                : JsonContent.Create(new { topic, title, message, tags = Tags, click }),
        };
        if (settings.NtfyToken(stored) is { } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await clients.CreateClient(HttpClientName).SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"ntfy antwortet mit {(int)response.StatusCode} {response.ReasonPhrase}", null, response.StatusCode);
        }
    }
}
