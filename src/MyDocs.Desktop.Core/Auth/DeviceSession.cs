using System.Net;
using System.Net.Http.Headers;
using MyDocs.ApiClient;
using MyDocs.Desktop.Core.Secrets;
using MyDocs.Shared;
using MyDocs.Shared.Devices;

namespace MyDocs.Desktop.Core.Auth;

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
}

/// <summary>
/// Verbindung des Geräts mit einer Instanz (41, 152): Anmeldung über den Systembrowser mit PKCE, das Refresh-Token im
/// Schlüsselspeicher, das Zugriffstoken nur im Speicher. Ohne Netz bleibt das Gerät verbunden (38); erst eine
/// abgelehnte Erneuerung (widerrufen, Konto geändert) trennt es.
/// </summary>
public sealed class DeviceSession(DesktopSettingsStore settings, ISecretStore secrets, TimeProvider time, Func<HttpMessageHandler>? handlers = null) : IDisposable
{
    private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RefreshEarly = TimeSpan.FromMinutes(1);
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessExpires;

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    public Uri? Server { get; private set; }

    public Guid? DeviceId { get; private set; }

    /// <summary>Verbindung oder Abmeldung hat sich geändert (die Oberfläche zeigt dann „Verbinden“ bzw. die App).</summary>
    public event Action? Changed;

    /// <summary>Beim Start: gespeicherte Verbindung übernehmen, wenn ein Refresh-Token da ist.</summary>
    public async Task<bool> RestoreAsync(CancellationToken ct)
    {
        var stored = settings.Load();
        if (stored.ServerUrl is not { } url || stored.DeviceId is not { } device || !Uri.TryCreate(url, UriKind.Absolute, out var server))
        {
            return false;
        }

        if (await secrets.GetAsync(SecretKey(server), ct) is null)
        {
            return false;
        }

        Server = server;
        DeviceId = device;
        SetState(ConnectionState.Connected);
        return true;
    }

    /// <summary>Anmeldung über den Systembrowser; <paramref name="openBrowser"/> öffnet die Freigabeseite.</summary>
    public async Task ConnectAsync(Uri server, string deviceName, Func<Uri, Task> openBrowser, string successPage, string failurePage, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(openBrowser);
        SetState(ConnectionState.Connecting);
        try
        {
            var pkce = Pkce.Create();
            var state = Pkce.NewState();
            using var receiver = new LoopbackReceiver();
            var authorize = new Uri(server, "device/authorize"
                + $"?redirect_uri={Uri.EscapeDataString(receiver.RedirectUri)}&code_challenge={pkce.Challenge}&code_challenge_method=S256"
                + $"&state={state}&device_name={Uri.EscapeDataString(deviceName)}");
            await openBrowser(authorize);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(SignInTimeout);
            var code = await receiver.WaitForCodeAsync(state, successPage, failurePage, timeout.Token);
            var tokens = await PlainClient(server).TokenAsync(new DeviceTokenRequest(DeviceGrants.AuthorizationCode, code, pkce.Verifier, receiver.RedirectUri), ct);
            await StoreAsync(server, tokens, ct);
            settings.Update(s => s with { ServerUrl = server.ToString(), DeviceId = tokens.DeviceId });
            Server = server;
            DeviceId = tokens.DeviceId;
            SetState(ConnectionState.Connected);
        }
        catch
        {
            SetState(ConnectionState.Disconnected);
            throw;
        }
    }

    /// <summary>Gültiges Zugriffstoken, bei Bedarf erneuert; null, wenn das Gerät nicht (mehr) verbunden ist.</summary>
    public async Task<string?> AccessTokenAsync(bool force, CancellationToken ct)
    {
        if (State != ConnectionState.Connected || Server is not { } server)
        {
            return null;
        }

        if (!force && _accessToken is { } current && _accessExpires - RefreshEarly > time.GetUtcNow())
        {
            return current;
        }

        await _refresh.WaitAsync(ct);
        try
        {
            if (!force && _accessToken is { } fresh && _accessExpires - RefreshEarly > time.GetUtcNow())
            {
                return fresh;
            }

            if (await secrets.GetAsync(SecretKey(server), ct) is not { } refresh)
            {
                await ForgetAsync(server, ct);
                return null;
            }

            try
            {
                var tokens = await PlainClient(server).TokenAsync(new DeviceTokenRequest(DeviceGrants.RefreshToken, RefreshToken: refresh), ct);
                await StoreAsync(server, tokens, ct);
                return tokens.AccessToken;
            }
            catch (ApiException exception) when (exception.Code == DeviceErrors.InvalidGrant || exception.Status == HttpStatusCode.Unauthorized)
            {
                // Widerrufen oder Konto geändert: neu anmelden.
                await ForgetAsync(server, ct);
                return null;
            }
        }
        finally
        {
            _refresh.Release();
        }
    }

    /// <summary>Abmelden: Gerät auf dem Server widerrufen (wenn erreichbar) und Token löschen.</summary>
    public async Task DisconnectAsync(CancellationToken ct)
    {
        if (Server is not { } server)
        {
            return;
        }

        try
        {
            if (DeviceId is { } device && await AccessTokenAsync(false, ct) is { } token)
            {
                using var client = PlainHttp(server);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var _ = await client.DeleteAsync(new Uri($"api/devices/{device}", UriKind.Relative), ct);
            }
        }
        catch (HttpRequestException)
        {
            // Ohne Netz bleibt das Gerät auf dem Server, bis es in der Web-App widerrufen wird.
        }

        await ForgetAsync(server, ct);
    }

    /// <summary>Platzhalter, bis eine Verbindung besteht; der Handler setzt beim Senden den echten Server ein.</summary>
    internal static readonly Uri Placeholder = new("https://mydocs.invalid/");

    /// <summary>
    /// HttpClient für die API mit dem Zugriffstoken (für <c>MyDocs.ApiClient</c> und die Oberfläche). Er entsteht auch
    /// ohne Verbindung; Anfragen scheitern dann mit <see cref="HttpRequestException"/>, wie ohne Netz.
    /// </summary>
    public HttpClient CreateApiClient()
    {
        var client = new HttpClient(new DeviceTokenHandler(this) { InnerHandler = handlers?.Invoke() ?? new SocketsHttpHandler() }) { BaseAddress = Placeholder };
        client.DefaultRequestHeaders.Add(ApiHeaders.Csrf, ApiHeaders.CsrfValue);
        client.Timeout = TimeSpan.FromMinutes(30);
        return client;
    }

    /// <summary>Der Server hat ein Token abgelehnt, das hier als gültig galt.</summary>
    internal void Invalidate() => _accessToken = null;

    public void Dispose() => _refresh.Dispose();

    private async Task StoreAsync(Uri server, DeviceTokenDto tokens, CancellationToken ct)
    {
        await secrets.SetAsync(SecretKey(server), tokens.RefreshToken, ct);
        _accessToken = tokens.AccessToken;
        _accessExpires = time.GetUtcNow().AddSeconds(tokens.ExpiresIn);
    }

    private async Task ForgetAsync(Uri server, CancellationToken ct)
    {
        _accessToken = null;
        await secrets.DeleteAsync(SecretKey(server), ct);
        settings.Update(s => s with { DeviceId = null });
        DeviceId = null;
        SetState(ConnectionState.Disconnected);
    }

    private void SetState(ConnectionState state)
    {
        State = state;
        Changed?.Invoke();
    }

    private HttpClient PlainHttp(Uri server)
    {
        var client = new HttpClient(handlers?.Invoke() ?? new SocketsHttpHandler(), disposeHandler: true) { BaseAddress = server };
        client.DefaultRequestHeaders.Add(ApiHeaders.Csrf, ApiHeaders.CsrfValue);
        return client;
    }

    private DeviceClient PlainClient(Uri server) => new(PlainHttp(server));

    private static string SecretKey(Uri server) => $"refresh:{server.GetLeftPart(UriPartial.Authority)}";
}

/// <summary>Setzt das Zugriffstoken; lehnt der Server es ab, wird es einmal erneuert und die Anfrage ohne Inhalt wiederholt.</summary>
public sealed class DeviceTokenHandler(DeviceSession session) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (session.Server is not { } server || session.State != ConnectionState.Connected)
        {
            throw new HttpRequestException("Nicht verbunden.");
        }

        // Relativ gebaute Adressen gehen an den verbundenen Server.
        if (request.RequestUri is { } uri && uri.IsAbsoluteUri && uri.Host == DeviceSession.Placeholder.Host)
        {
            request.RequestUri = new Uri(server, uri.PathAndQuery.TrimStart('/'));
        }

        if (await session.AccessTokenAsync(false, cancellationToken) is { } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized || request.Content is not null)
        {
            return response;
        }

        session.Invalidate();
        if (await session.AccessTokenAsync(true, cancellationToken) is not { } renewed)
        {
            return response;
        }

        response.Dispose();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", renewed);
        return await base.SendAsync(request, cancellationToken);
    }
}
