using System.Net;
using System.Net.Http.Json;
using MyDocs.Shared.Auth;

namespace MyDocs.ApiClient;

/// <summary>Einrichtung, Anmeldung und eigenes Konto.</summary>
public sealed class AuthClient(HttpClient http)
{
    public async Task<SetupStatusDto> GetSetupStatusAsync(CancellationToken ct = default) =>
        await http.GetFromJsonAsync<SetupStatusDto>("api/setup/status", ct) ?? new SetupStatusDto(false);

    public Task<TotpSetupDto> StartSetupAsync(SetupStartRequest request, CancellationToken ct = default) =>
        PostAsync<SetupStartRequest, TotpSetupDto>("api/setup/start", request, ct);

    public Task<RecoveryCodesDto> FinishSetupAsync(SetupFinishRequest request, CancellationToken ct = default) =>
        PostAsync<SetupFinishRequest, RecoveryCodesDto>("api/setup/finish", request, ct);

    public Task<LoginResultDto> LoginAsync(LoginRequest request, CancellationToken ct = default) =>
        PostAsync<LoginRequest, LoginResultDto>("api/auth/login", request, ct);

    public async Task LoginTwoFactorAsync(LoginTwoFactorRequest request, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync("api/auth/login/2fa", request, ct);
        await ApiException.EnsureSuccessAsync(response, ct);
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        using var response = await http.PostAsync(new Uri("api/auth/logout", UriKind.Relative), content: null, ct);
        await ApiException.EnsureSuccessAsync(response, ct);
    }

    /// <summary>Das angemeldete Konto oder null ohne Anmeldung.</summary>
    public async Task<MeDto?> GetMeAsync(CancellationToken ct = default)
    {
        using var response = await http.GetAsync(new Uri("api/auth/me", UriKind.Relative), ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return null;
        }

        await ApiException.EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<MeDto>(ct);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest request, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(path, request, ct);
        await ApiException.EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<TResponse>(ct))!;
    }
}
