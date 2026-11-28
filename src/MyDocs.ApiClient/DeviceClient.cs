using MyDocs.Shared.Devices;

namespace MyDocs.ApiClient;

/// <summary>Geräte (M4.1): Freigabe im Browser, eigene Geräte, Widerruf.</summary>
public sealed class DeviceClient(HttpClient http)
{
    public Task<DeviceAuthorizeResult> AuthorizeAsync(DeviceAuthorizeRequest request, CancellationToken ct = default) =>
        http.SendAsync<DeviceAuthorizeRequest, DeviceAuthorizeResult>(HttpMethod.Post, "api/devices/authorize", request, ct);

    public Task<List<DeviceDto>> ListAsync(CancellationToken ct = default) => http.GetAsync<List<DeviceDto>>("api/devices", ct);

    public Task RevokeAsync(Guid id, CancellationToken ct = default) => http.SendAsync(HttpMethod.Delete, $"api/devices/{id}", ct);

    /// <summary>Tausch gegen Tokens (Desktop-Client, ohne Anmeldung).</summary>
    public Task<DeviceTokenDto> TokenAsync(DeviceTokenRequest request, CancellationToken ct = default) =>
        http.SendAsync<DeviceTokenRequest, DeviceTokenDto>(HttpMethod.Post, "api/devices/token", request, ct);
}
