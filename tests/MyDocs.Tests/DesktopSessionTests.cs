using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Web;
using MyDocs.Desktop.Core;
using MyDocs.Desktop.Core.Auth;
using MyDocs.Desktop.Core.Secrets;
using MyDocs.Shared.Auth;
using MyDocs.Shared.Devices;
using MyDocs.Shared.Household;

namespace MyDocs.Tests;

/// <summary>Schlüsselspeicher im Speicher für Tests.</summary>
public sealed class MemorySecretStore : ISecretStore
{
    public ConcurrentDictionary<string, string> Secrets { get; } = new(StringComparer.Ordinal);

    public Task<string?> GetAsync(string key, CancellationToken ct) => Task.FromResult(Secrets.TryGetValue(key, out var value) ? value : null);

    public Task SetAsync(string key, string secret, CancellationToken ct)
    {
        Secrets[key] = secret;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        Secrets.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}

/// <summary>Anmeldung des Desktop-Clients von seiner Seite (M4.1): Systembrowser, Loopback, Token, Widerruf.</summary>
public sealed class DesktopSessionTests(ServerFixture server) : IClassFixture<ServerFixture>, IAsyncLifetime
{
    private static readonly Uri Server = new("https://localhost/");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static int _counter;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mydocs-desktop-tests", Guid.NewGuid().ToString("N"));
    private HttpClient _admin = null!;
    private HttpClient _bea = null!;

    public async ValueTask InitializeAsync()
    {
        var n = Interlocked.Increment(ref _counter);
        _admin = await TestAccounts.AdminAsync(server);
        _bea = await TestAccounts.CreateAsync(server, _admin, $"desk-bea{n}@example.test", $"Bea {n}", HouseholdRoles.Member);
    }

    public ValueTask DisposeAsync()
    {
        _admin.Dispose();
        _bea.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    internal DeviceSession Session(MemorySecretStore secrets) =>
        new(new DesktopSettingsStore(new DesktopPaths(_root)), secrets, server.Clock, () => server.Server.CreateHandler());

    /// <summary>Spielt den Browser: Freigabe mit dem Cookie von Bea (oder Ablehnung), dann Aufruf der Loopback-Adresse.</summary>
    internal static Func<Uri, Task> Browser(HttpClient person, bool allow = true) => authorize =>
    {
        _ = Task.Run(async () =>
        {
            var query = HttpUtility.ParseQueryString(authorize.Query);
            string target;
            if (allow)
            {
                var request = new DeviceAuthorizeRequest(query["redirect_uri"]!, query["code_challenge"]!, query["code_challenge_method"]!, query["device_name"]!, query["state"]);
                var response = await person.PostAsJsonAsync("/api/devices/authorize", request);
                target = (await response.Content.ReadFromJsonAsync<DeviceAuthorizeResult>())!.RedirectUrl;
            }
            else
            {
                target = $"{query["redirect_uri"]}?error=access_denied&state={query["state"]}";
            }

            using var loopback = new HttpClient();
            await loopback.GetAsync(new Uri(target));
        });
        return Task.CompletedTask;
    };

    [Fact]
    public async Task TheClientConnectsKeepsOnlyTheRefreshTokenAndDisconnects()
    {
        var secrets = new MemorySecretStore();
        using var session = Session(secrets);

        await session.ConnectAsync(Server, "Testrechner", Browser(_bea), "ok", "fehler", Ct);

        Assert.Equal(ConnectionState.Connected, session.State);
        Assert.Single(secrets.Secrets);
        Assert.DoesNotContain("Token", File.ReadAllText(Path.Combine(_root, "settings.json")), StringComparison.OrdinalIgnoreCase);
        using (var api = session.CreateApiClient())
        {
            Assert.Equal((await TestAccounts.MeAsync(_bea)).Id, (await api.GetFromJsonAsync<MeDto>("api/auth/me", Ct))!.Id);
        }

        // Neuer Start: Verbindung aus Einstellungen und Schlüsselspeicher, das Zugriffstoken kommt per Refresh.
        using var restarted = Session(secrets);
        Assert.True(await restarted.RestoreAsync(Ct));
        using (var api = restarted.CreateApiClient())
        {
            Assert.Equal(HttpStatusCode.OK, (await api.GetAsync(new Uri("api/auth/me", UriKind.Relative), Ct)).StatusCode);
        }

        await restarted.DisconnectAsync(Ct);
        Assert.Equal(ConnectionState.Disconnected, restarted.State);
        Assert.Empty(secrets.Secrets);
        Assert.DoesNotContain((await _bea.GetFromJsonAsync<List<DeviceDto>>("/api/devices", Ct))!, d => d.Name == "Testrechner");
    }

    [Fact]
    public async Task ARevokedDeviceFallsBackToConnect()
    {
        var secrets = new MemorySecretStore();
        using var session = Session(secrets);
        await session.ConnectAsync(Server, "Wird widerrufen", Browser(_bea), "ok", "fehler", Ct);
        var changes = 0;
        session.Changed += () => changes++;

        Assert.Equal(HttpStatusCode.NoContent, (await _bea.DeleteAsync($"/api/devices/{session.DeviceId}", Ct)).StatusCode);
        using var api = session.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.GetAsync(new Uri("api/auth/me", UriKind.Relative), Ct)).StatusCode);

        Assert.Equal(ConnectionState.Disconnected, session.State);
        Assert.Equal(1, changes);
        Assert.Empty(secrets.Secrets);
    }

    [Fact]
    public async Task ADeniedSignInLeavesTheClientDisconnected()
    {
        using var session = Session(new MemorySecretStore());

        var failure = await Assert.ThrowsAsync<SignInFailedException>(() => session.ConnectAsync(Server, "Abgelehnt", Browser(_bea, allow: false), "ok", "fehler", Ct));

        Assert.Equal("access_denied", failure.Reason);
        Assert.Equal(ConnectionState.Disconnected, session.State);
    }

    [Fact]
    public void PkceMatchesWhatTheServerChecks()
    {
        var pkce = Pkce.Create();
        Assert.True(DeviceRules.IsBase64Url(pkce.Verifier, 43, 128));
        Assert.True(DeviceRules.IsBase64Url(pkce.Challenge, 43, 128));
        Assert.NotEqual(Pkce.Create().Verifier, pkce.Verifier);
    }
}
