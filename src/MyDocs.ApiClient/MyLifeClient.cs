using MyDocs.Shared.MyLife;

namespace MyDocs.ApiClient;

/// <summary>Brücke zu myLife (M5.1): Verbindung im eigenen Konto und die Markierungen an Einträgen und Dokumenten.</summary>
public sealed class MyLifeClient(HttpClient http)
{
    public Task<MyLifeLinkDto> StatusAsync(CancellationToken ct = default) => http.GetAsync<MyLifeLinkDto>("api/account/mylife", ct);

    public Task<MyLifeConnectResult> ConnectAsync(CancellationToken ct = default) =>
        http.PostForAsync<MyLifeConnectResult>("api/account/mylife", ct);

    public Task DisconnectAsync(CancellationToken ct = default) => http.SendAsync(HttpMethod.Delete, "api/account/mylife", ct);

    public Task SetObjectAsync(Guid id, bool enabled, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Put, $"api/objects/{id}/mylife", new MyLifeFlagRequest(enabled), ct);

    public Task SetDocumentAsync(Guid id, bool enabled, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Put, $"api/documents/{id}/mylife", new MyLifeFlagRequest(enabled), ct);
}
