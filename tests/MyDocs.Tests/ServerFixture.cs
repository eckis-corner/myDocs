using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MyDocs.Server.Infrastructure;
using MyDocs.Server.Infrastructure.Auth;
using MyDocs.Shared;
using MyDocs.Shared.Auth;
using Testcontainers.PostgreSql;

namespace MyDocs.Tests;

/// <summary>Startet PostgreSQL in einem Container und den Server dagegen.</summary>
public sealed class ServerFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "mydocs-tests", Guid.NewGuid().ToString("N"));

    public string DataDirectory => _dataDirectory;

    /// <summary>Uhr des Servers; Tests stellen sie vor, etwa für den nächsten TOTP-Schritt.</summary>
    public TestClock Clock { get; } = new();

    /// <summary>Zugangsdaten des Administrators aus <see cref="EnsureAdminAsync"/>.</summary>
    public const string AdminEmail = "admin@example.test";

    public const string AdminPassword = "ein langes Testpasswort";

    private readonly SemaphoreSlim _setupLock = new(1, 1);
    private AdminAccount? _admin;

    /// <summary>Client wie der Browser: HTTPS (Secure-Cookie), Cookies und der CSRF-Header für schreibende Aufrufe.</summary>
    public HttpClient CreateApiClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        client.DefaultRequestHeaders.Add(ApiHeaders.Csrf, ApiHeaders.CsrfValue);
        return client;
    }

    /// <summary>Aktueller Einrichtungscode, wie er im Protokoll steht.</summary>
    public string? SetupCode => Services.GetRequiredService<SetupTokenService>().CurrentToken;

    /// <summary>Code der Authenticator-App zur Uhr des Servers; stellt die Uhr vorher einen Schritt weiter, damit der Code neu ist.</summary>
    public string NextTotp(string secret)
    {
        Clock.Advance(TimeSpan.FromSeconds(Totp.PeriodSeconds));
        return Totp.Compute(Base32.Decode(secret), Totp.StepAt(Clock.GetUtcNow()));
    }

    /// <summary>Richtet myDocs einmal ein (Administrator mit TOTP) und liefert Geheimnis und Wiederherstellungscodes.</summary>
    public async Task<AdminAccount> EnsureAdminAsync()
    {
        await _setupLock.WaitAsync();
        try
        {
            if (_admin is not null)
            {
                return _admin;
            }

            using var client = CreateApiClient();
            var start = await client.PostAsJsonAsync("/api/setup/start", new SetupStartRequest(SetupCode!, AdminEmail, "Admin", AdminPassword));
            start.EnsureSuccessStatusCode();
            var totp = (await start.Content.ReadFromJsonAsync<TotpSetupDto>())!;
            var finish = await client.PostAsJsonAsync("/api/setup/finish", new SetupFinishRequest(SetupCode!, AdminEmail, NextTotp(totp.Secret)));
            finish.EnsureSuccessStatusCode();
            var codes = (await finish.Content.ReadFromJsonAsync<RecoveryCodesDto>())!;
            return _admin = new AdminAccount(totp.Secret, [.. codes.Codes]);
        }
        finally
        {
            _setupLock.Release();
        }
    }

    public async ValueTask InitializeAsync() => await _database.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("ConnectionStrings:MyDocs", _database.GetConnectionString());
        builder.UseSetting("MyDocs:DataDirectory", _dataDirectory);
        // Die Tests melden sich viel öfter an als ein Mensch; das Limit gegen Durchprobieren würde sonst greifen.
        builder.UseSetting("MyDocs:AuthRequestsPerMinute", "10000");
        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(Clock));
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
        _setupLock.Dispose();
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }
}

public sealed record AdminAccount(string TotpSecret, List<string> RecoveryCodes);

/// <summary>
/// Einstellbare Uhr für Tests. Startet bei der echten Zeit, gekappt auf ganze Mikrosekunden (Genauigkeit von PostgreSQL).
/// </summary>
public sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = TruncateToMicroseconds(DateTimeOffset.UtcNow);

    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMicrosecond));

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
