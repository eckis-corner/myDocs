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

// Notfall-Befehle von der Kommandozeile, bei gestoppter App: restore, master-key restore (M1.7b).
if (MyDocs.Server.Infrastructure.Backup.BackupCli.Handles(args))
{
    return await MyDocs.Server.Infrastructure.Backup.BackupCli.RunAsync(args, builder.Configuration, options);
}

var dataDirectory = Path.GetFullPath(options.DataDirectory);
Directory.CreateDirectory(Path.Combine(dataDirectory, "keys"));
builder.Services.AddDataProtection()
    .SetApplicationName("myDocs")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")));

MyDocsDbContextFactory.Configure(builder.Services, builder.Configuration.GetConnectionString("MyDocs"));

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

// Desktop-Client (41, 152): Bearer-Token je Gerät neben dem Cookie; die Wahl trifft jede Anfrage selbst.
builder.Services.AddSingleton<DeviceTokens>();
builder.Services.AddAuthentication()
    .AddPolicyScheme(DeviceTokens.SelectorScheme, null, policy => policy.ForwardDefaultSelector = context =>
        context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.Ordinal) ? DeviceTokens.Scheme : IdentityConstants.ApplicationScheme)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DeviceAuthenticationHandler>(DeviceTokens.Scheme, null);
builder.Services.Configure<Microsoft.AspNetCore.Authentication.AuthenticationOptions>(authentication =>
{
    authentication.DefaultScheme = DeviceTokens.SelectorScheme;
    authentication.DefaultAuthenticateScheme = DeviceTokens.SelectorScheme;
    authentication.DefaultChallengeScheme = DeviceTokens.SelectorScheme;
    authentication.DefaultForbidScheme = DeviceTokens.SelectorScheme;
});

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
// Neue Rechte, Sperre und Zurücksetzen wirken spätestens nach einer Minute auf laufende Sitzungen.
builder.Services.Configure<SecurityStampValidatorOptions>(stamp => stamp.ValidationInterval = TimeSpan.FromMinutes(1));
builder.Services.AddScoped<OneTimeTokens>();
builder.Services.AddScoped<MyDocs.Server.Infrastructure.Objects.ObjectAccess>();
builder.Services.AddScoped<MyDocs.Server.Infrastructure.Tasks.TaskAccess>();
builder.Services.AddScoped<MyDocs.Server.Infrastructure.Tasks.ReviewEngine>();
builder.Services.AddHostedService<MyDocs.Server.Infrastructure.Tasks.ReviewService>();
builder.Services.AddSingleton<MyDocs.Server.Infrastructure.Files.MasterKey>();
builder.Services.AddScoped<MyDocs.Server.Infrastructure.Files.BlobStore>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<MyDocs.Server.Infrastructure.Audit.AuditLog>();
builder.Services.AddSingleton<TrashPurge>();
builder.Services.AddHostedService(services => services.GetRequiredService<TrashPurge>());
builder.Services.AddSingleton<MyDocs.Server.Infrastructure.Backup.BackupService>();
builder.Services.AddHostedService(services => services.GetRequiredService<MyDocs.Server.Infrastructure.Backup.BackupService>());
// Einstellungen, Mail und ntfy (M2.4)
builder.Services.AddSingleton<MyDocs.Server.Infrastructure.Settings.SettingsStore>();
builder.Services.AddSingleton<MyDocs.Server.Infrastructure.Mail.MailSignal>();
builder.Services.AddSingleton<MyDocs.Server.Infrastructure.Mail.IMailTransport, MyDocs.Server.Infrastructure.Mail.SmtpMailTransport>();
builder.Services.AddScoped<MyDocs.Server.Infrastructure.Mail.MailOutbox>();
builder.Services.AddSingleton<MyDocs.Server.Infrastructure.Mail.MailOutboxService>();
builder.Services.AddHostedService(services => services.GetRequiredService<MyDocs.Server.Infrastructure.Mail.MailOutboxService>());
builder.Services.AddHttpClient(MyDocs.Server.Infrastructure.Mail.NtfyPushSender.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddScoped<MyDocs.Server.Infrastructure.Mail.IPushSender, MyDocs.Server.Infrastructure.Mail.NtfyPushSender>();
builder.Services.AddScoped<MyDocs.Server.Infrastructure.Notifications.Notifier>();
builder.Services.AddHostedService<MyDocs.Server.Infrastructure.Notifications.NotificationService>();
// Verarbeitung im Worker (M3.1): OCR kann bei langen Scans dauern.
builder.Services.AddSingleton<MyDocs.Server.Infrastructure.Processing.ProcessingSignal>();
builder.Services.AddScoped<MyDocs.Server.Infrastructure.Processing.ProcessingScheduler>();
builder.Services.AddScoped<MyDocs.Server.Infrastructure.Processing.DocumentProcessor>();
builder.Services.AddHttpClient(MyDocs.Server.Infrastructure.Processing.HttpDocumentWorker.HttpClientName, client => client.Timeout = TimeSpan.FromMinutes(35));
builder.Services.AddSingleton<MyDocs.Server.Infrastructure.Processing.IDocumentWorker, MyDocs.Server.Infrastructure.Processing.HttpDocumentWorker>();
builder.Services.AddSingleton<MyDocs.Server.Infrastructure.Processing.ProcessingService>();
builder.Services.AddHostedService(services => services.GetRequiredService<MyDocs.Server.Infrastructure.Processing.ProcessingService>());
// Scan-Ordner (M3.5)
builder.Services.AddSingleton<MyDocs.Server.Infrastructure.Inbox.ScanFolderService>();
builder.Services.AddHostedService(services => services.GetRequiredService<MyDocs.Server.Infrastructure.Inbox.ScanFolderService>());
// Mail-Eingang (M3.6)
builder.Services.AddSingleton<MyDocs.Server.Infrastructure.Mail.IMailboxConnector, MyDocs.Server.Infrastructure.Mail.ImapMailboxConnector>();
builder.Services.AddSingleton<MyDocs.Server.Infrastructure.Inbox.MailIntakeService>();
builder.Services.AddHostedService(services => services.GetRequiredService<MyDocs.Server.Infrastructure.Inbox.MailIntakeService>());
// Uploads bis zur Grenze aus den Einstellungen (78) plus Platz für den Multipart-Rahmen.
var maxRequestBytes = (options.MaxFileSizeMegabytes + 1) * 1024L * 1024L;
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Roles.Admin, policy => policy.RequireRole(Roles.Admin))
    .AddPolicy(PersonEndpoints.MemberPolicy, policy => policy.RequireRole(MyDocs.Shared.Household.HouseholdRoles.Member));

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
app.MapActivationEndpoints();
app.MapAdminUserEndpoints();
app.MapPersonEndpoints();
app.MapObjectEndpoints();
app.MapDocumentEndpoints(maxRequestBytes);
app.MapSearchEndpoints();
app.MapAuditEndpoints();
app.MapAccountEndpoints();
app.MapTaskEndpoints();
app.MapDashboardEndpoints();
app.MapAdminBackupEndpoints();
app.MapSettingsEndpoints();
app.MapAdminProcessingEndpoints();
app.MapInboxEndpoints(maxRequestBytes);
app.MapDeviceEndpoints();
app.MapCheckoutEndpoints(maxRequestBytes);

app.MapStaticAssets();
// Die Seiten sind nur die leere Hülle (kein Prerendering); [Authorize] prüft der Client. Geschützt sind die Daten:
// Jeder API-Endpunkt verlangt die Anmeldung selbst (SecurityTests).
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(MyDocs.UI.Routes).Assembly)
    .AllowAnonymous();

await DatabaseMigrator.MigrateAsync(app.Services);
// Gespeicherte Einstellungen gelten ab dem Start, auch für Upload und Sicherung.
await app.Services.GetRequiredService<MyDocs.Server.Infrastructure.Settings.SettingsStore>().GetAsync();
// Hauptschlüssel beim Start anlegen bzw. prüfen, nicht erst beim ersten Upload.
app.Services.GetRequiredService<MyDocs.Server.Infrastructure.Files.MasterKey>();
await MyDocs.Server.Infrastructure.Search.SearchIndex.BackfillAsync(app.Services);

await app.RunAsync();
return 0;

static Task WriteStatusAsync(HttpResponse response, int status, string code)
{
    response.StatusCode = status;
    return response.WriteAsJsonAsync(new { title = code, status });
}

/// <summary>Für WebApplicationFactory in den Tests.</summary>
public partial class Program;
