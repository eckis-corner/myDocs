using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using MyDocs.Server.Api;
using MyDocs.Server.Components;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure;
using MyDocs.Server.Infrastructure.Auth;
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
    db.UseNpgsql(builder.Configuration.GetConnectionString("MyDocs")).UseSnakeCaseNamingConvention());

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
builder.Services.AddSingleton<SetupTokenService>();
builder.Services.AddHostedService<SetupTokenIssuer>();

// ASP.NET Core Identity (40): Passwort plus zweiter Faktor für alle, Sperre nach Fehlversuchen, Tokens verschlüsselt.
builder.Services.AddIdentity<AppUser, IdentityRole<Guid>>(identity =>
    {
        identity.Password.RequiredLength = MyDocs.Shared.Auth.PasswordRules.MinLength;
        identity.Password.RequireDigit = false;
        identity.Password.RequireLowercase = false;
        identity.Password.RequireUppercase = false;
        identity.Password.RequireNonAlphanumeric = false;
        identity.Lockout.MaxFailedAccessAttempts = 5;
        identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        identity.User.RequireUniqueEmail = true;
        identity.Tokens.AuthenticatorTokenProvider = OneTimeTotpProvider.Name;
        identity.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddEntityFrameworkStores<MyDocsDbContext>()
    .AddUserStore<ProtectedUserStore>()
    .AddTokenProvider<OneTimeTotpProvider>(OneTimeTotpProvider.Name);

// Sitzungs-Cookie wie myLife: HttpOnly, SameSite=Strict, gleitende Laufzeit. Die API antwortet mit 401/403 statt umzuleiten.
builder.Services.ConfigureApplicationCookie(cookie =>
{
    cookie.Cookie.Name = "mydocs.session";
    cookie.Cookie.HttpOnly = true;
    cookie.Cookie.SameSite = SameSiteMode.Strict;
    cookie.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    cookie.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    cookie.SlidingExpiration = true;
    cookie.Events.OnRedirectToLogin = context => WriteStatusAsync(context.Response, StatusCodes.Status401Unauthorized, "auth-required");
    cookie.Events.OnRedirectToAccessDenied = context => WriteStatusAsync(context.Response, StatusCodes.Status403Forbidden, "auth-forbidden");
});
builder.Services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, cookie =>
{
    cookie.Cookie.Name = "mydocs.2fa";
    cookie.Cookie.HttpOnly = true;
    cookie.Cookie.SameSite = SameSiteMode.Strict;
    cookie.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    cookie.ExpireTimeSpan = TimeSpan.FromMinutes(5);
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Roles.Admin, policy => policy.RequireRole(Roles.Admin));

builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.AddPolicy(SetupEndpoints.RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
    limiter.AddPolicy(AuthEndpoints.RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = options.AuthRequestsPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

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
app.UseMiddleware<CsrfHeaderMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapInfoEndpoints();
app.MapSetupEndpoints();
app.MapAuthEndpoints();

app.MapStaticAssets();
// Die Seiten sind nur die leere Hülle (kein Prerendering); [Authorize] prüft der Client. Geschützt sind die Daten:
// Jeder API-Endpunkt verlangt die Anmeldung selbst (SecurityTests).
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(MyDocs.UI.Routes).Assembly)
    .AllowAnonymous();

await DatabaseMigrator.MigrateAsync(app.Services);

await app.RunAsync();

static Task WriteStatusAsync(HttpResponse response, int status, string code)
{
    response.StatusCode = status;
    return response.WriteAsJsonAsync(new { title = code, status });
}

/// <summary>Für WebApplicationFactory in den Tests.</summary>
public partial class Program;
