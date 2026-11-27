using System.Net;
using System.Net.Http.Json;
using MyDocs.Shared.Audit;
using MyDocs.Shared.Auth;
using MyDocs.Shared.Household;

namespace MyDocs.Tests;

/// <summary>Passkeys als zweiter Faktor (M1.2b), mit vollständiger WebAuthn-Zeremonie über einen Authenticator in Software.</summary>
public sealed class PasskeyTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(HttpClient Client, string Email)> MemberAsync()
    {
        using var admin = await TestAccounts.AdminAsync(server);
        var email = $"passkey-{Guid.NewGuid():N}@example.test";
        return (await TestAccounts.CreateAsync(server, admin, email, "Pia", HouseholdRoles.Member), email);
    }

    private static async Task RegisterAsync(HttpClient client, SoftwareAuthenticator authenticator, string name)
    {
        var options = (await (await client.PostAsync(new Uri("/api/account/passkeys/options", UriKind.Relative), null, Ct)).Content.ReadFromJsonAsync<PasskeyOptionsDto>(Ct))!;
        var added = await client.PostAsJsonAsync("/api/account/passkeys", new PasskeyCredentialRequest(authenticator.Create(options.OptionsJson), name), Ct);
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
    }

    private static async Task<HttpResponseMessage> LoginWithPasskeyAsync(HttpClient client, string email, SoftwareAuthenticator authenticator)
    {
        var login = (await (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, TestAccounts.Password), Ct)).Content.ReadFromJsonAsync<LoginResultDto>(Ct))!;
        Assert.True(login.TwoFactorRequired);
        Assert.True(login.PasskeyAvailable);
        var options = (await (await client.PostAsync(new Uri("/api/auth/login/passkey/options", UriKind.Relative), null, Ct)).Content.ReadFromJsonAsync<PasskeyOptionsDto>(Ct))!;
        return await client.PostAsJsonAsync("/api/auth/login/passkey", new PasskeyCredentialRequest(authenticator.Get(options.OptionsJson)), Ct);
    }

    [Fact]
    public async Task APasskeyReplacesTheCodeAfterThePassword()
    {
        var (member, email) = await MemberAsync();
        using var authenticator = new SoftwareAuthenticator("https://localhost");
        await RegisterAsync(member, authenticator, "Laptop");

        var passkeys = (await member.GetFromJsonAsync<List<PasskeyDto>>("/api/account/passkeys", Ct))!;
        Assert.Equal("Laptop", Assert.Single(passkeys).Name);

        using var fresh = server.CreateApiClient();
        var signedIn = await LoginWithPasskeyAsync(fresh, email, authenticator);
        Assert.Equal(HttpStatusCode.NoContent, signedIn.StatusCode);
        Assert.Equal(email, (await TestAccounts.MeAsync(fresh)).Email);
        Assert.Contains((await fresh.GetFromJsonAsync<List<AuditEntryDto>>("/api/audit", Ct))!, e => e.Action == AuditActions.Login && e.Details == "Passkey");
    }

    [Fact]
    public async Task AForgedSignatureIsRejected()
    {
        var (member, email) = await MemberAsync();
        using var authenticator = new SoftwareAuthenticator("https://localhost");
        await RegisterAsync(member, authenticator, "Telefon");
        authenticator.ForgeNextSignature = true;

        using var fresh = server.CreateApiClient();
        var response = await LoginWithPasskeyAsync(fresh, email, authenticator);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await fresh.GetAsync(new Uri("/api/auth/me", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task APasskeyAloneIsNotEnough()
    {
        var (member, _) = await MemberAsync();
        using var authenticator = new SoftwareAuthenticator("https://localhost");
        await RegisterAsync(member, authenticator, "Stick");

        using var stranger = server.CreateApiClient();
        var options = await stranger.PostAsync(new Uri("/api/auth/login/passkey/options", UriKind.Relative), null, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, options.StatusCode);
    }

    [Fact]
    public async Task ARemovedPasskeyNoLongerWorks()
    {
        var (member, email) = await MemberAsync();
        using var authenticator = new SoftwareAuthenticator("https://localhost");
        await RegisterAsync(member, authenticator, "Alt");
        var id = Assert.Single((await member.GetFromJsonAsync<List<PasskeyDto>>("/api/account/passkeys", Ct))!).Id;

        Assert.Equal(HttpStatusCode.NoContent, (await member.DeleteAsync(new Uri($"/api/account/passkeys/{id}", UriKind.Relative), Ct)).StatusCode);

        using var fresh = server.CreateApiClient();
        var login = (await (await fresh.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, TestAccounts.Password), Ct)).Content.ReadFromJsonAsync<LoginResultDto>(Ct))!;
        Assert.False(login.PasskeyAvailable);
    }
}
