using MyDocs.Shared.Notifications;

namespace MyDocs.ApiClient;

/// <summary>Eigene Benachrichtigungen: Sprache, tägliche Zusammenfassung, ntfy-Thema.</summary>
public sealed class NotificationClient(HttpClient http)
{
    public Task<NotificationSettingsDto> GetAsync(CancellationToken ct = default) => http.GetAsync<NotificationSettingsDto>("api/account/notifications", ct);

    public Task<NotificationSettingsDto> SaveAsync(NotificationSettingsRequest request, CancellationToken ct = default) =>
        http.SendAsync<NotificationSettingsRequest, NotificationSettingsDto>(HttpMethod.Put, "api/account/notifications", request, ct);

    public Task TestAsync(CancellationToken ct = default) => http.SendAsync(HttpMethod.Post, "api/account/notifications/test", ct);
}
