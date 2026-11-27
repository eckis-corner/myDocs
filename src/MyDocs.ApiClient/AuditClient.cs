using MyDocs.Shared.Audit;

namespace MyDocs.ApiClient;

/// <summary>Protokoll: eigenes und (für den Administrator) das ganze.</summary>
public sealed class AuditClient(HttpClient http)
{
    public Task<List<AuditEntryDto>> MineAsync(CancellationToken ct = default) => http.GetAsync<List<AuditEntryDto>>("api/audit", ct);

    public Task<List<AuditEntryDto>> AllAsync(CancellationToken ct = default) => http.GetAsync<List<AuditEntryDto>>("api/admin/audit", ct);
}
