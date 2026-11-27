using MyDocs.Shared.Dashboard;

namespace MyDocs.ApiClient;

/// <summary>Startseite.</summary>
public sealed class DashboardClient(HttpClient http)
{
    public Task<DashboardDto> GetAsync(bool mine, CancellationToken ct = default) => http.GetAsync<DashboardDto>($"api/dashboard?scope={(mine ? "mine" : "household")}", ct);
}
