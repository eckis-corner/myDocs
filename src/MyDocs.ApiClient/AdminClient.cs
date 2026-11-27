using MyDocs.Shared.Admin;

namespace MyDocs.ApiClient;

/// <summary>Benutzerverwaltung (nur Administrator).</summary>
public sealed class AdminClient(HttpClient http)
{
    public Task<List<AdminUserDto>> GetUsersAsync(CancellationToken ct = default) => http.GetAsync<List<AdminUserDto>>("api/admin/users", ct);

    public Task<ActivationLinkDto> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default) =>
        http.SendAsync<CreateUserRequest, ActivationLinkDto>(HttpMethod.Post, "api/admin/users", request, ct);

    public Task<AdminUserDto> UpdateUserAsync(Guid id, UpdateUserRequest request, CancellationToken ct = default) =>
        http.SendAsync<UpdateUserRequest, AdminUserDto>(HttpMethod.Put, $"api/admin/users/{id}", request, ct);

    public Task LockAsync(Guid id, CancellationToken ct = default) => http.SendAsync(HttpMethod.Post, $"api/admin/users/{id}/lock", ct);

    public Task UnlockAsync(Guid id, CancellationToken ct = default) => http.SendAsync(HttpMethod.Post, $"api/admin/users/{id}/unlock", ct);

    public Task<ActivationLinkDto> ResetAsync(Guid id, CancellationToken ct = default) => http.PostForAsync<ActivationLinkDto>($"api/admin/users/{id}/reset", ct);

    public Task<BackupStatusDto> BackupStatusAsync(CancellationToken ct = default) => http.GetAsync<BackupStatusDto>("api/admin/backup", ct);

    public Task<BackupItemDto> BackupNowAsync(CancellationToken ct = default) => http.PostForAsync<BackupItemDto>("api/admin/backup", ct);

    public Task<MasterKeyDto> MasterKeyAsync(string code, CancellationToken ct = default) =>
        http.SendAsync<MasterKeyRequest, MasterKeyDto>(HttpMethod.Post, "api/admin/master-key", new MasterKeyRequest(code), ct);
}
