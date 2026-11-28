using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using MyDocs.Shared;
using MyDocs.Shared.Admin;
using MyDocs.Shared.Audit;
using MyDocs.Shared.Auth;
using MyDocs.Shared.Devices;
using MyDocs.Shared.Household;

namespace MyDocs.Tests;

/// <summary>Anmeldung des Desktop-Clients (M4.1): PKCE, Loopback, Tokens, Erneuerung, Widerruf.</summary>
public sealed class DeviceTests(ServerFixture server) : IClassFixture<ServerFixture>, IAsyncLifetime
{
    private const string Redirect = "http://127.0.0.1:53682/callback";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static int _counter;
    private HttpClient _admin = null!;
    private HttpClient _bea = null!;
    private Guid _beaId;

    public async ValueTask InitializeAsync()
    {
        var n = Interlocked.Increment(ref _counter);
        _admin = await TestAccounts.AdminAsync(server);
        _bea = await TestAccounts.CreateAsync(server, _admin, $"device-bea{n}@example.test", $"Bea {n}", HouseholdRoles.Member);
        _beaId = (await TestAccounts.MeAsync(_bea)).Id;
    }

    public ValueTask DisposeAsync()
    {
        _admin.Dispose();
        _bea.Dispose();
        return ValueTask.CompletedTask;
    }

    internal static (string Verifier, string Challenge) Pkce()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        return (verifier, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static async Task<string> AuthorizeAsync(HttpClient browser, string challenge, string redirect = Redirect, string state = "abc123")
    {
        var response = await browser.PostAsJsonAsync("/api/devices/authorize", new DeviceAuthorizeRequest(redirect, challenge, "S256", "Beas Laptop (macOS)", state), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var url = new Uri((await response.Content.ReadFromJsonAsync<DeviceAuthorizeResult>(Ct))!.RedirectUrl);
        Assert.StartsWith(redirect + "?code=", url.ToString(), StringComparison.Ordinal);
        Assert.Contains($"state={state}", url.Query, StringComparison.Ordinal);
        return Uri.UnescapeDataString(url.Query.Split('&')[0]["?code=".Length..]);
    }

    private HttpClient Anonymous()
    {
        var client = server.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        client.DefaultRequestHeaders.Add(ApiHeaders.Csrf, ApiHeaders.CsrfValue);
        return client;
    }

    private HttpClient Bearer(string accessToken)
    {
        var client = Anonymous();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    internal static async Task<DeviceTokenDto> ConnectAsync(ServerFixture server, HttpClient browser)
    {
        var (verifier, challenge) = Pkce();
        var code = await AuthorizeAsync(browser, challenge);
        using var anonymous = server.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        anonymous.DefaultRequestHeaders.Add(ApiHeaders.Csrf, ApiHeaders.CsrfValue);
        var response = await anonymous.PostAsJsonAsync("/api/devices/token", new DeviceTokenRequest(DeviceGrants.AuthorizationCode, code, verifier, Redirect), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DeviceTokenDto>(Ct))!;
    }

    private async Task<HttpResponseMessage> TokenAsync(DeviceTokenRequest request)
    {
        using var anonymous = Anonymous();
        return await anonymous.PostAsJsonAsync("/api/devices/token", request, Ct);
    }

    [Fact]
    public async Task ADeviceSignsInThroughTheBrowserAndThenWorksWithItsToken()
    {
        var tokens = await ConnectAsync(server, _bea);

        using var device = Bearer(tokens.AccessToken);
        var me = (await device.GetFromJsonAsync<MeDto>("/api/auth/me", Ct))!;
        Assert.Equal(_beaId, me.Id);
        Assert.Equal(HouseholdRoles.Member, me.Role);
        var devices = (await device.GetFromJsonAsync<List<DeviceDto>>("/api/devices", Ct))!;
        var current = Assert.Single(devices, d => d.Current);
        Assert.Equal(("Beas Laptop (macOS)", tokens.DeviceId), (current.Name, current.Id));
        // Schreiben geht auch, wie im Browser mit dem CSRF-Header.
        Assert.Equal(HttpStatusCode.OK, (await device.PostAsJsonAsync("/api/persons", new PersonRequest("Kind", null, null), Ct)).StatusCode);
        var audit = (await _bea.GetFromJsonAsync<List<AuditEntryDto>>("/api/audit", Ct))!;
        Assert.Contains(audit, a => a.Action == AuditActions.DeviceAdded && a.Title == "Beas Laptop (macOS)");
    }

    [Fact]
    public async Task CodesNeedTheRightVerifierAndWorkOnlyOnce()
    {
        var (verifier, challenge) = Pkce();
        var code = await AuthorizeAsync(_bea, challenge);

        Assert.Equal(HttpStatusCode.BadRequest, (await TokenAsync(new DeviceTokenRequest(DeviceGrants.AuthorizationCode, code, Pkce().Verifier, Redirect))).StatusCode);
        // Der erste, gescheiterte Versuch hat den Code verbraucht.
        Assert.Equal(HttpStatusCode.BadRequest, (await TokenAsync(new DeviceTokenRequest(DeviceGrants.AuthorizationCode, code, verifier, Redirect))).StatusCode);

        var second = await AuthorizeAsync(_bea, challenge);
        Assert.Equal(HttpStatusCode.BadRequest, (await TokenAsync(new DeviceTokenRequest(DeviceGrants.AuthorizationCode, second, verifier, "http://127.0.0.1:1/anders"))).StatusCode);

        var late = await AuthorizeAsync(_bea, challenge);
        server.Clock.Advance(TimeSpan.FromMinutes(3));
        Assert.Equal(HttpStatusCode.BadRequest, (await TokenAsync(new DeviceTokenRequest(DeviceGrants.AuthorizationCode, late, verifier, Redirect))).StatusCode);
    }

    [Theory]
    [InlineData("https://evil.example/callback")]
    [InlineData("http://127.0.0.1/callback")]
    [InlineData("http://192.168.1.5:5000/callback")]
    [InlineData("http://127.0.0.1:5000/callback?x=1")]
    [InlineData("myapp://callback")]
    public async Task OnlyLoopbackAddressesReceiveCodes(string redirect)
    {
        var response = await _bea.PostAsJsonAsync("/api/devices/authorize", new DeviceAuthorizeRequest(redirect, Pkce().Challenge, "S256", "Gerät", null), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OnlyS256AndNoDeviceAuthorizesAnother()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await _bea.PostAsJsonAsync("/api/devices/authorize", new DeviceAuthorizeRequest(Redirect, Pkce().Verifier, "plain", "Gerät", null), Ct)).StatusCode);
        var tokens = await ConnectAsync(server, _bea);
        using var device = Bearer(tokens.AccessToken);
        var again = await device.PostAsJsonAsync("/api/devices/authorize", new DeviceAuthorizeRequest(Redirect, Pkce().Challenge, "S256", "Noch eins", null), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, again.StatusCode);
    }

    [Fact]
    public async Task RefreshRotatesTheTokenAndExpiredAccessTokensAreRefused()
    {
        var tokens = await ConnectAsync(server, _bea);
        server.Clock.Advance(TimeSpan.FromMinutes(16));
        using (var expired = Bearer(tokens.AccessToken))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await expired.GetAsync("/api/auth/me", Ct)).StatusCode);
        }

        var refreshed = await TokenAsync(new DeviceTokenRequest(DeviceGrants.RefreshToken, RefreshToken: tokens.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var next = (await refreshed.Content.ReadFromJsonAsync<DeviceTokenDto>(Ct))!;
        Assert.Equal(tokens.DeviceId, next.DeviceId);
        Assert.NotEqual(tokens.RefreshToken, next.RefreshToken);
        using var device = Bearer(next.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await device.GetAsync("/api/auth/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await TokenAsync(new DeviceTokenRequest(DeviceGrants.RefreshToken, RefreshToken: tokens.RefreshToken))).StatusCode);
    }

    [Fact]
    public async Task ARevokedDeviceStopsAtOnce()
    {
        var tokens = await ConnectAsync(server, _bea);
        using var device = Bearer(tokens.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, (await _bea.DeleteAsync($"/api/devices/{tokens.DeviceId}", Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await device.GetAsync("/api/auth/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await TokenAsync(new DeviceTokenRequest(DeviceGrants.RefreshToken, RefreshToken: tokens.RefreshToken))).StatusCode);
        Assert.DoesNotContain((await _bea.GetFromJsonAsync<List<DeviceDto>>("/api/devices", Ct))!, d => d.Id == tokens.DeviceId);
        // Fremde Geräte lassen sich nicht widerrufen.
        var other = await ConnectAsync(server, _bea);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.DeleteAsync($"/api/devices/{other.DeviceId}", Ct)).StatusCode);
    }

    [Fact]
    public async Task LockingTheAccountEndsItsDevices()
    {
        var tokens = await ConnectAsync(server, _bea);

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.PostAsync(new Uri($"/api/admin/users/{_beaId}/lock", UriKind.Relative), null, Ct)).StatusCode);

        using var device = Bearer(tokens.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await device.GetAsync("/api/auth/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await TokenAsync(new DeviceTokenRequest(DeviceGrants.RefreshToken, RefreshToken: tokens.RefreshToken))).StatusCode);
    }

    [Fact]
    public async Task ForgedTokensAreRefused()
    {
        using var forged = Bearer("kein-token");
        Assert.Equal(HttpStatusCode.Unauthorized, (await forged.GetAsync("/api/auth/me", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await forged.GetAsync("/api/objects", Ct)).StatusCode);
    }
}
