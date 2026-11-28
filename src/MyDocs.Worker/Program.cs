using MyDocs.Worker;

// mydocs-worker (24, 136): zustandslos, ohne Datenbank und ohne Schlüssel. Die App schickt den Klartext einer Datei,
// der Worker antwortet mit PDF, Text oder Vorschaubild. Er hängt nur im internen Netz der App und erreicht das Internet
// nicht, damit Office-Dateien keine fremden Inhalte nachladen.
var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.AddServerHeader = false;
    kestrel.Limits.MaxRequestBodySize = Tools.MaxInputBytes;
});
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Services.AddSingleton<Tools>();

var app = builder.Build();

app.MapGet("/health", () => Results.Text("ok"));
app.MapPost(WorkerPaths.Pdf, (HttpRequest request, string source, string? ext, Tools tools, CancellationToken ct) =>
    tools.RunAsync(request, "in" + Tools.CleanExtension(ext), "application/pdf", (input, output, token) => tools.ToPdfAsync(source, input, output, token), ct));
app.MapPost(WorkerPaths.Text, (HttpRequest request, Tools tools, CancellationToken ct) =>
    tools.RunAsync(request, "in.pdf", "text/plain; charset=utf-8", tools.TextAsync, ct));
app.MapPost(WorkerPaths.Thumbnail, (HttpRequest request, Tools tools, CancellationToken ct) =>
    tools.RunAsync(request, "in.pdf", "image/png", tools.ThumbnailAsync, ct));

await app.RunAsync();
