using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using MyDocs.Server.Api;
using MyDocs.Server.Components;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
// Kein „Server: Kestrel“ in den Antworten.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false);

builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture));

var options = builder.Configuration.GetSection(MyDocsOptions.SectionName).Get<MyDocsOptions>() ?? new MyDocsOptions();
builder.Services.Configure<MyDocsOptions>(builder.Configuration.GetSection(MyDocsOptions.SectionName));

var dataDirectory = Path.GetFullPath(options.DataDirectory);
Directory.CreateDirectory(Path.Combine(dataDirectory, "keys"));
builder.Services.AddDataProtection()
    .SetApplicationName("myDocs")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")));

builder.Services.AddDbContext<MyDocsDbContext>(db =>
    db.UseNpgsql(builder.Configuration.GetConnectionString("MyDocs")));

builder.Services.AddHealthChecks()
    .AddDbContextCheck<MyDocsDbContext>("database", tags: ["ready"]);

builder.Services.Configure<ForwardedHeadersOptions>(forwarded =>
{
    forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    forwarded.KnownIPNetworks.Clear();
    forwarded.KnownProxies.Clear();
    foreach (var network in options.TrustedProxyNetworks)
    {
        forwarded.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    }
});

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

var app = builder.Build();

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseSerilogRequestLogging();
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapInfoEndpoints();

app.MapStaticAssets();
// Die Seiten sind nur die leere Hülle (kein Prerendering). Geschützt sind ab M1.2 die Daten: Jeder API-Endpunkt
// verlangt die Anmeldung selbst.
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(MyDocs.UI.Routes).Assembly)
    .AllowAnonymous();

await DatabaseMigrator.MigrateAsync(app.Services);

await app.RunAsync();

/// <summary>Für WebApplicationFactory in den Tests.</summary>
public partial class Program;
