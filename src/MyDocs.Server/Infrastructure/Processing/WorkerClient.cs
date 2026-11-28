using System.Net;
using System.Net.Http.Headers;

namespace MyDocs.Server.Infrastructure.Processing;

/// <summary>Eingabe an den Worker: schreibt den Klartext direkt in die Anfrage, ohne ihn ganz im Speicher zu halten.</summary>
public sealed record WorkerInput(Func<Stream, CancellationToken, Task> WriteAsync, long Length);

/// <summary>Der Worker hat die Datei abgelehnt (Werkzeug mit Fehler, Zeit überschritten); ein neuer Versuch ändert nichts.</summary>
public sealed class WorkerRejectedException(string message) : Exception(message);

/// <summary>Der Worker ist nicht erreichbar oder hatte einen inneren Fehler; später noch einmal versuchen.</summary>
public sealed class WorkerUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Die Werkzeuge im Container <c>mydocs-worker</c> (24). In den Tests ersetzt durch eine Attrappe.</summary>
public interface IDocumentWorker
{
    bool IsConfigured { get; }

    /// <summary>PDF aus der Datei; <paramref name="source"/> ist <c>pdf</c>, <c>image</c> oder <c>office</c>.</summary>
    Task<T> PdfAsync<T>(WorkerInput input, string source, string extension, Func<Stream, Task<T>> consume, CancellationToken ct);

    Task<string> TextAsync(WorkerInput pdf, CancellationToken ct);

    Task<T> ThumbnailAsync<T>(WorkerInput pdf, Func<Stream, Task<T>> consume, CancellationToken ct);

    Task<bool> IsHealthyAsync(CancellationToken ct);
}

/// <summary>HTTP-Client für den Worker im internen Netz der App.</summary>
public sealed class HttpDocumentWorker(IHttpClientFactory clients, Microsoft.Extensions.Options.IOptions<MyDocsOptions> options) : IDocumentWorker
{
    public const string HttpClientName = "worker";

    // Dieselben Pfade wie MyDocs.Worker.WorkerPaths.
    private const string PdfPath = "/v1/pdf";
    private const string TextPath = "/v1/text";
    private const string ThumbnailPath = "/v1/thumbnail";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.WorkerUrl);

    public Task<T> PdfAsync<T>(WorkerInput input, string source, string extension, Func<Stream, Task<T>> consume, CancellationToken ct) =>
        PostAsync($"{PdfPath}?source={Uri.EscapeDataString(source)}&ext={Uri.EscapeDataString(extension)}", input, consume, ct);

    public Task<string> TextAsync(WorkerInput pdf, CancellationToken ct) =>
        PostAsync(TextPath, pdf, async stream =>
        {
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            return await reader.ReadToEndAsync(ct);
        }, ct);

    public Task<T> ThumbnailAsync<T>(WorkerInput pdf, Func<Stream, Task<T>> consume, CancellationToken ct) => PostAsync(ThumbnailPath, pdf, consume, ct);

    public async Task<bool> IsHealthyAsync(CancellationToken ct)
    {
        if (!IsConfigured)
        {
            return false;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var response = await Client().GetAsync(new Uri("/health", UriKind.Relative), timeout.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            return false;
        }
    }

    private HttpClient Client()
    {
        var client = clients.CreateClient(HttpClientName);
        client.BaseAddress = new Uri(options.Value.WorkerUrl!);
        return client;
    }

    private async Task<T> PostAsync<T>(string path, WorkerInput input, Func<Stream, Task<T>> consume, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            using var content = new CallbackContent(input);
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative)) { Content = content };
            response = await Client().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException exception)
        {
            throw new WorkerUnavailableException("Worker nicht erreichbar", exception);
        }
        catch (TaskCanceledException exception) when (!ct.IsCancellationRequested)
        {
            throw new WorkerUnavailableException("Worker antwortet nicht", exception);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(ct);
                throw new WorkerRejectedException(problem?.Title ?? "abgelehnt");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new WorkerUnavailableException($"Worker: {(int)response.StatusCode}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            return await consume(stream);
        }
    }

    private sealed record ProblemBody(string? Title);

    private sealed class CallbackContent : HttpContent
    {
        private readonly WorkerInput _input;

        public CallbackContent(WorkerInput input)
        {
            _input = input;
            Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => _input.WriteAsync(stream, CancellationToken.None);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
            _input.WriteAsync(stream, cancellationToken);

        protected override bool TryComputeLength(out long length)
        {
            length = _input.Length;
            return true;
        }
    }
}
