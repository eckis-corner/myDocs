using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MyDocs.Worker;

/// <summary>Pfade der Worker-API; die App nutzt dieselben (MyDocs.Server, WorkerClient).</summary>
public static class WorkerPaths
{
    public const string Pdf = "/v1/pdf";
    public const string Text = "/v1/text";
    public const string Thumbnail = "/v1/thumbnail";
}

/// <summary>Ein Werkzeug ist mit Fehler beendet worden; die App versucht es mit derselben Datei nicht noch einmal.</summary>
public sealed class ToolFailedException(string message) : Exception(message);

/// <summary>
/// Die Werkzeuge des Workers: LibreOffice (Office → PDF), img2pdf und OCRmyPDF mit Tesseract (Bild/Scan → PDF/A mit
/// Textebene), pdftotext und pdftoppm (Text, Vorschaubild). Immer nur eine Datei gleichzeitig (24). Jede Anfrage
/// bekommt ein eigenes Verzeichnis unter <c>/tmp</c>, das im Container ein tmpfs ist, und es wird danach gelöscht.
/// </summary>
public sealed partial class Tools(ILogger<Tools> logger) : IDisposable
{
    public const long MaxInputBytes = 1L << 30;

    private static readonly TimeSpan OcrTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan OfficeTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMinutes(2);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public void Dispose() => _gate.Dispose();

    public static string CleanExtension(string? extension)
    {
        var value = (extension ?? string.Empty).Trim().ToLowerInvariant();
        return ExtensionPattern().IsMatch(value) ? value : ".bin";
    }

    /// <summary>Schreibt die Anfrage in eine Datei, führt die Arbeit aus und streamt das Ergebnis zurück.</summary>
    public async Task<IResult> RunAsync(HttpRequest request, string inputName, string contentType, Func<string, string, CancellationToken, Task> work, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        var directory = Path.Combine(Path.GetTempPath(), "mydocs-" + Guid.NewGuid().ToString("N"));
        var handedOver = false;
        try
        {
            Directory.CreateDirectory(directory);
            var input = Path.Combine(directory, inputName);
            await using (var file = new FileStream(input, FileMode.CreateNew, FileAccess.Write))
            {
                await request.Body.CopyToAsync(file, ct);
            }

            var output = Path.Combine(directory, "out");
            await work(input, output, ct);
            handedOver = true;
            return Results.Stream(async body =>
            {
                try
                {
                    await using var file = File.OpenRead(output);
                    await file.CopyToAsync(body, ct);
                }
                finally
                {
                    Cleanup(directory);
                    _gate.Release();
                }
            }, contentType);
        }
        catch (ToolFailedException failure)
        {
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: failure.Message);
        }
        finally
        {
            if (!handedOver)
            {
                Cleanup(directory);
                _gate.Release();
            }
        }
    }

    /// <summary>
    /// PDF aus einer Datei: Office und Text über LibreOffice; Bilder über img2pdf (auf A4 eingepasst) und dann wie
    /// Scans über OCRmyPDF: Seiten drehen, gerade richten, kleine Bilder auf 300 dpi hochrechnen, Text erkennen (de, en),
    /// PDF/A. Seiten mit Text bleiben, wie sie sind. Scheitert die Erkennung bei einem Bild, kommt das PDF ohne Text zurück.
    /// </summary>
    public async Task ToPdfAsync(string source, string input, string output, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(input)!;
        switch (source)
        {
            case "office":
                var target = Path.Combine(directory, "lo");
                await RunToolAsync("soffice", ["--headless", "--norestore", "--nolockcheck", "--nodefault",
                    "-env:UserInstallation=file://" + Path.Combine(directory, "profile"), "--convert-to", "pdf", "--outdir", target, input], directory, OfficeTimeout, ct);
                var pdf = Path.Combine(target, Path.GetFileNameWithoutExtension(input) + ".pdf");
                if (!File.Exists(pdf))
                {
                    throw new ToolFailedException("soffice: kein PDF erzeugt");
                }

                File.Move(pdf, output);
                break;
            case "image":
                var pages = Path.Combine(directory, "image.pdf");
                await RunToolAsync("img2pdf", ["--pagesize", "A4", "--fit", "into", "--output", pages, input], directory, ShortTimeout, ct);
                try
                {
                    await OcrAsync(pages, output, directory, ct);
                }
                catch (ToolFailedException)
                {
                    // Ohne erkannten Text gibt es wenigstens Vorschau und Vorschaubild.
                    File.Move(pages, output, overwrite: true);
                }

                break;
            case "pdf":
                await OcrAsync(input, output, directory, ct);
                break;
            default:
                throw new ToolFailedException("Unbekannte Quelle");
        }
    }

    /// <summary>Text eines PDF (pdftotext, UTF-8); Seiten sind durch Seitenvorschub (\f) getrennt.</summary>
    public Task TextAsync(string input, string output, CancellationToken ct) =>
        RunToolAsync("pdftotext", ["-enc", "UTF-8", input, output], Path.GetDirectoryName(input)!, ShortTimeout, ct);

    /// <summary>Vorschaubild der ersten Seite, höchstens 480 Pixel, als PNG.</summary>
    public async Task ThumbnailAsync(string input, string output, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(input)!;
        var prefix = Path.Combine(directory, "thumb");
        await RunToolAsync("pdftoppm", ["-png", "-f", "1", "-l", "1", "-singlefile", "-scale-to", "480", input, prefix], directory, ShortTimeout, ct);
        File.Move(prefix + ".png", output);
    }

    private Task OcrAsync(string input, string output, string directory, CancellationToken ct) =>
        RunToolAsync("ocrmypdf", ["--language", "deu+eng", "--output-type", "pdfa", "--rotate-pages", "--deskew", "--clean",
            "--skip-text", "--oversample", "300", "--jobs", "1", "--optimize", "1", "--quiet", input, output], directory, OcrTimeout, ct);

    private async Task RunToolAsync(string tool, IReadOnlyList<string> arguments, string directory, TimeSpan timeout, CancellationToken ct)
    {
        var start = new ProcessStartInfo(tool)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = directory,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        // LibreOffice und Tesseract legen Einstellungen im Home ab; es soll mit der Anfrage verschwinden.
        start.Environment["HOME"] = directory;
        start.Environment["TMPDIR"] = directory;
        var watch = Stopwatch.StartNew();
        using var process = Process.Start(start) ?? throw new ToolFailedException($"{tool}: nicht gestartet");
        var stderr = process.StandardError.ReadToEndAsync(ct);
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timer.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timer.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            if (ct.IsCancellationRequested)
            {
                throw;
            }

            throw new ToolFailedException($"{tool}: Zeit überschritten");
        }

        await stdout;
        var error = (await stderr).Trim();
        LogTool(logger, tool, process.ExitCode, watch.ElapsedMilliseconds);
        if (process.ExitCode != 0)
        {
            var tail = error.Length > 300 ? error[^300..] : error;
            throw new ToolFailedException($"{tool} ({process.ExitCode}): {tail}");
        }
    }

    private static void Cleanup(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Beim nächsten Neustart ist /tmp (tmpfs) ohnehin leer.
        }
    }

    [GeneratedRegex("^\\.[a-z0-9]{1,8}$")]
    private static partial Regex ExtensionPattern();

    [LoggerMessage(Level = LogLevel.Information, Message = "{Tool}: Ende {ExitCode} nach {Milliseconds} ms")]
    private static partial void LogTool(ILogger logger, string tool, int exitCode, long milliseconds);
}
