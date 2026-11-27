using System.Net;
using System.Net.Http.Json;

namespace MyDocs.Tests;

/// <summary>Schein-Server für Seitentests: Antworten je Pfad, sonst 404. Merkt sich die Aufrufe.</summary>
public sealed class FakeApi : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new(StringComparer.Ordinal);

    public List<string> Calls { get; } = [];

    public FakeApi On(string methodAndPath, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _routes[methodAndPath] = respond;
        return this;
    }

    public FakeApi OnJson<T>(string methodAndPath, T body, HttpStatusCode status = HttpStatusCode.OK) =>
        On(methodAndPath, _ => new HttpResponseMessage(status) { Content = JsonContent.Create(body) });

    public FakeApi OnProblem(string methodAndPath, string code, HttpStatusCode status) =>
        OnJson(methodAndPath, new { title = code, status = (int)status }, status);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var key = $"{request.Method} {request.RequestUri!.AbsolutePath}";
        Calls.Add(key);
        return Task.FromResult(_routes.TryGetValue(key, out var respond) ? respond(request) : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
