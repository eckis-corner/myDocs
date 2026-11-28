using System.Collections.Concurrent;
using System.Text;
using MyDocs.Server.Infrastructure.Processing;

namespace MyDocs.Tests;

/// <summary>
/// Attrappe des Workers: „OCR“ setzt eine Kennung vor den Inhalt, „Text“ liefert den Inhalt nach der Kennung zurück.
/// Inhalte mit SCAN haben keine Textebene, Inhalte mit REJECT lehnt die PDF-Erzeugung ab.
/// </summary>
public sealed class FakeWorker : IDocumentWorker
{
    public const string OcrMarker = "%PDF-1.7 OCR\n";

    public static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public bool IsConfigured { get; set; } = true;

    /// <summary>Wie ein gestoppter Container: Jede Anfrage scheitert.</summary>
    public bool Unavailable { get; set; }

    public ConcurrentQueue<string> Calls { get; } = new();

    public async Task<T> PdfAsync<T>(WorkerInput input, string source, string extension, Func<Stream, Task<T>> consume, CancellationToken ct)
    {
        var text = await EnterAsync($"pdf:{source}:{extension}", input, ct);
        if (text.Contains("REJECT", StringComparison.Ordinal))
        {
            throw new WorkerRejectedException("ocrmypdf (2): kaputt");
        }

        return await consume(new MemoryStream(Encoding.UTF8.GetBytes(OcrMarker + text.Replace("SCAN", string.Empty, StringComparison.Ordinal))));
    }

    public async Task<string> TextAsync(WorkerInput pdf, CancellationToken ct)
    {
        var text = await EnterAsync("text", pdf, ct);
        return text.StartsWith(OcrMarker, StringComparison.Ordinal) ? text[OcrMarker.Length..] + "\f"
            : text.Contains("SCAN", StringComparison.Ordinal) ? "\f"
            : text + "\f";
    }

    public async Task<T> ThumbnailAsync<T>(WorkerInput pdf, Func<Stream, Task<T>> consume, CancellationToken ct)
    {
        var text = await EnterAsync("thumbnail", pdf, ct);
        return await consume(new MemoryStream([.. PngSignature, .. Encoding.UTF8.GetBytes(text[..Math.Min(text.Length, 40)])]));
    }

    public Task<bool> IsHealthyAsync(CancellationToken ct) => Task.FromResult(IsConfigured && !Unavailable);

    private async Task<string> EnterAsync(string call, WorkerInput input, CancellationToken ct)
    {
        Calls.Enqueue(call);
        if (Unavailable)
        {
            throw new WorkerUnavailableException("Worker nicht erreichbar");
        }

        using var buffer = new MemoryStream();
        await input.WriteAsync(buffer, ct);
        Assert.Equal(input.Length, buffer.Length);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
