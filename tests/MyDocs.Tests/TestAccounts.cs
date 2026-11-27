using System.Net;
using System.Net.Http.Json;
using MyDocs.Shared.Admin;
using MyDocs.Shared.Auth;

namespace MyDocs.Tests;

/// <summary>Angemeldete Clients für Tests: Administrator und weitere Konten über den Aktivierungslink.</summary>
internal static class TestAccounts
{
    public const string Password = "ein langes Passwort";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<HttpClient> AdminAsync(ServerFixture server)
    {
        var account = await server.EnsureAdminAsync();
        var client = server.CreateApiClient();
        await LoginAsync(server, client, ServerFixture.AdminEmail, account.TotpSecret, ServerFixture.AdminPassword);
        return client;
    }

    /// <summary>Legt ein Konto an, aktiviert es und liefert einen angemeldeten Client.</summary>
    public static async Task<HttpClient> CreateAsync(ServerFixture server, HttpClient admin, string email, string name, string role)
    {
        var response = await admin.PostAsJsonAsync("/api/admin/users", new CreateUserRequest(email, name, role, false), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var link = (await response.Content.ReadFromJsonAsync<ActivationLinkDto>(Ct))!;
        var client = server.CreateApiClient();
        var start = await client.PostAsJsonAsync("/api/activation/start", new ActivationStartRequest(link.Token, Password), Ct);
        var totp = (await start.Content.ReadFromJsonAsync<TotpSetupDto>(Ct))!;
        var finish = await client.PostAsJsonAsync("/api/activation/finish", new ActivationFinishRequest(link.Token, server.NextTotp(totp.Secret)), Ct);
        Assert.Equal(HttpStatusCode.OK, finish.StatusCode);
        return client;
    }

    public static async Task LoginAsync(ServerFixture server, HttpClient client, string email, string secret, string password = Password)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password), Ct);
        Assert.True((await login.Content.ReadFromJsonAsync<LoginResultDto>(Ct))!.TwoFactorRequired);
        var second = await client.PostAsJsonAsync("/api/auth/login/2fa", new LoginTwoFactorRequest(server.NextTotp(secret), false), Ct);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
    }

    public static async Task<MeDto> MeAsync(HttpClient client) => (await client.GetFromJsonAsync<MeDto>("/api/auth/me", Ct))!;
}
