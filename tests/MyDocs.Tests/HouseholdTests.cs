using System.Net;
using System.Net.Http.Json;
using MyDocs.Shared.Admin;
using MyDocs.Shared.Auth;
using MyDocs.Shared.Household;

namespace MyDocs.Tests;

/// <summary>Benutzer, Aktivierung, Rollen und Personen (M1.3).</summary>
public sealed class HouseholdTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private const string Password = "ein langes Passwort";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheAdminCreatesAnAccountThatActivatesItselfViaTheLink()
    {
        using var admin = await AdminClientAsync();
        var link = await CreateAsync(admin, "anna@example.test", "Anna", HouseholdRoles.Member);

        using var anna = server.CreateApiClient();
        var info = await (await anna.PostAsJsonAsync("/api/activation/info", new ActivationTokenRequest(link.Token), Ct)).Content.ReadFromJsonAsync<ActivationInfoDto>(Ct);
        Assert.Equal(("anna@example.test", "Anna"), (info!.Email, info.DisplayName));

        // Vor der Aktivierung kann sich niemand mit dem Konto anmelden.
        var early = await anna.PostAsJsonAsync("/api/auth/login", new LoginRequest("anna@example.test", Password), Ct);
        Assert.NotEqual(HttpStatusCode.OK, early.StatusCode);

        var secret = await ActivateAsync(anna, link.Token);
        var me = (await anna.GetFromJsonAsync<MeDto>("/api/auth/me", Ct))!;
        Assert.Equal((HouseholdRoles.Member, false), (me.Role, me.IsAdmin));
        Assert.NotNull(me.PersonId);

        // Der Link gilt nur einmal.
        var again = await anna.PostAsJsonAsync("/api/activation/info", new ActivationTokenRequest(link.Token), Ct);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);

        // Anmelden wie alle anderen: Passwort und Einmal-Code.
        using var later = server.CreateApiClient();
        await LoginAsync(later, "anna@example.test", secret);
        Assert.Equal("Anna", (await later.GetFromJsonAsync<MeDto>("/api/auth/me", Ct))!.DisplayName);

        var users = await admin.GetFromJsonAsync<List<AdminUserDto>>("/api/admin/users", Ct);
        Assert.Equal(UserStates.Active, users!.Single(u => u.Email == "anna@example.test").State);
    }

    [Fact]
    public async Task GuestsReadButOnlyMembersChangePersons()
    {
        using var admin = await AdminClientAsync();
        var child = await admin.PostAsJsonAsync("/api/persons", new PersonRequest("Ben", new DateOnly(2015, 3, 1), "Sohn"), Ct);
        Assert.Equal(HttpStatusCode.OK, child.StatusCode);

        using var guest = server.CreateApiClient();
        var link = await CreateAsync(admin, "gast@example.test", "Oma", HouseholdRoles.Guest);
        await ActivateAsync(guest, link.Token);

        var persons = (await guest.GetFromJsonAsync<List<PersonDto>>("/api/persons", Ct))!;
        Assert.Contains(persons, p => p.DisplayName == "Ben" && p.UserId is null);
        Assert.Contains(persons, p => p.DisplayName == "Oma" && p.UserId is not null);

        Assert.Equal(HttpStatusCode.Forbidden, (await guest.PostAsJsonAsync("/api/persons", new PersonRequest("X", null, null), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.GetAsync(new Uri("/api/admin/users", UriKind.Relative), Ct)).StatusCode);

        // Personen mit Konto lassen sich nicht entfernen, Personen ohne Konto schon.
        var oma = persons.Single(p => p.DisplayName == "Oma");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync(new Uri($"/api/persons/{oma.Id}", UriKind.Relative), Ct)).StatusCode);
        var ben = persons.Single(p => p.DisplayName == "Ben");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(new Uri($"/api/persons/{ben.Id}", UriKind.Relative), Ct)).StatusCode);
        Assert.DoesNotContain(await admin.GetFromJsonAsync<List<PersonDto>>("/api/persons", Ct) ?? [], p => p.Id == ben.Id);
    }

    [Fact]
    public async Task ALockedAccountCannotLogInUntilUnlocked()
    {
        using var admin = await AdminClientAsync();
        var link = await CreateAsync(admin, "carl@example.test", "Carl", HouseholdRoles.Member);
        using var carl = server.CreateApiClient();
        var secret = await ActivateAsync(carl, link.Token);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync(new Uri($"/api/admin/users/{link.UserId}/lock", UriKind.Relative), null, Ct)).StatusCode);
        using var blocked = server.CreateApiClient();
        var login = await blocked.PostAsJsonAsync("/api/auth/login", new LoginRequest("carl@example.test", Password), Ct);
        Assert.Equal(AuthErrors.Disabled, await SetupTests.TitleAsync(login));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync(new Uri($"/api/admin/users/{link.UserId}/unlock", UriKind.Relative), null, Ct)).StatusCode);
        using var again = server.CreateApiClient();
        await LoginAsync(again, "carl@example.test", secret);
    }

    [Fact]
    public async Task ResettingAccessReplacesPasswordAndSecondFactor()
    {
        using var admin = await AdminClientAsync();
        var link = await CreateAsync(admin, "dora@example.test", "Dora", HouseholdRoles.Member);
        using var dora = server.CreateApiClient();
        await ActivateAsync(dora, link.Token);

        var reset = (await (await admin.PostAsync(new Uri($"/api/admin/users/{link.UserId}/reset", UriKind.Relative), null, Ct)).Content.ReadFromJsonAsync<ActivationLinkDto>(Ct))!;

        using var old = server.CreateApiClient();
        var login = await old.PostAsJsonAsync("/api/auth/login", new LoginRequest("dora@example.test", Password), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);

        using var fresh = server.CreateApiClient();
        await ActivateAsync(fresh, reset.Token);
        Assert.Equal("Dora", (await fresh.GetFromJsonAsync<MeDto>("/api/auth/me", Ct))!.DisplayName);
    }

    [Fact]
    public async Task TheAdminCannotLockOrDemoteThemself()
    {
        using var admin = await AdminClientAsync();
        var me = (await admin.GetFromJsonAsync<MeDto>("/api/auth/me", Ct))!;

        var lockSelf = await admin.PostAsync(new Uri($"/api/admin/users/{me.Id}/lock", UriKind.Relative), null, Ct);
        Assert.Equal(HouseholdErrors.NotSelf, await SetupTests.TitleAsync(lockSelf));

        var demote = await admin.PutAsJsonAsync($"/api/admin/users/{me.Id}", new UpdateUserRequest("Admin", HouseholdRoles.Member, false), Ct);
        Assert.Equal(HouseholdErrors.NotSelf, await SetupTests.TitleAsync(demote));

        var guestAdmin = await admin.PostAsJsonAsync("/api/admin/users", new CreateUserRequest("x@example.test", "X", HouseholdRoles.Guest, true), Ct);
        Assert.Equal(HouseholdErrors.RoleInvalid, await SetupTests.TitleAsync(guestAdmin));

        var duplicate = await admin.PostAsJsonAsync("/api/admin/users", new CreateUserRequest(ServerFixture.AdminEmail, "Y", HouseholdRoles.Member, false), Ct);
        Assert.Equal(HouseholdErrors.EmailTaken, await SetupTests.TitleAsync(duplicate));
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        var account = await server.EnsureAdminAsync();
        var client = server.CreateApiClient();
        await LoginAsync(client, ServerFixture.AdminEmail, account.TotpSecret, ServerFixture.AdminPassword);
        return client;
    }

    private static async Task<ActivationLinkDto> CreateAsync(HttpClient admin, string email, string name, string role)
    {
        var response = await admin.PostAsJsonAsync("/api/admin/users", new CreateUserRequest(email, name, role, false), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ActivationLinkDto>(Ct))!;
    }

    /// <summary>Aktiviert das Konto über den Link und liefert das TOTP-Geheimnis.</summary>
    private async Task<string> ActivateAsync(HttpClient client, string token)
    {
        var start = await client.PostAsJsonAsync("/api/activation/start", new ActivationStartRequest(token, Password), Ct);
        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        var totp = (await start.Content.ReadFromJsonAsync<TotpSetupDto>(Ct))!;
        var finish = await client.PostAsJsonAsync("/api/activation/finish", new ActivationFinishRequest(token, server.NextTotp(totp.Secret)), Ct);
        Assert.Equal(HttpStatusCode.OK, finish.StatusCode);
        return totp.Secret;
    }

    private async Task LoginAsync(HttpClient client, string email, string secret, string password = Password)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password), Ct);
        Assert.True((await login.Content.ReadFromJsonAsync<LoginResultDto>(Ct))!.TwoFactorRequired);
        var second = await client.PostAsJsonAsync("/api/auth/login/2fa", new LoginTwoFactorRequest(server.NextTotp(secret), false), Ct);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
    }
}
