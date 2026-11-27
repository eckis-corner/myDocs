using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyDocs.Server.Data;
using MyDocs.Shared.Auth;

namespace MyDocs.Tests;

/// <summary>Einrichtung von Grund auf, mit eigener Datenbank.</summary>
public sealed class SetupTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SetupNeedsTheCodeAndASecondFactorAndHappensOnlyOnce()
    {
        using var client = server.CreateApiClient();
        Assert.True((await client.GetFromJsonAsync<SetupStatusDto>("/api/setup/status", Ct))!.Required);

        var wrongCode = await client.PostAsJsonAsync("/api/setup/start", new SetupStartRequest("AAAA-BBBB", "a@example.test", "A", "ein langes Passwort"), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, wrongCode.StatusCode);

        var code = server.SetupCode!;
        var shortPassword = await client.PostAsJsonAsync("/api/setup/start", new SetupStartRequest(code, "a@example.test", "A", "kurz"), Ct);
        Assert.Equal(AuthErrors.PasswordWeak, await TitleAsync(shortPassword));

        var start = await client.PostAsJsonAsync("/api/setup/start", new SetupStartRequest(code, "a@example.test", "Anna", "ein langes Passwort"), Ct);
        var totp = (await start.Content.ReadFromJsonAsync<TotpSetupDto>(Ct))!;
        Assert.StartsWith("otpauth://totp/myDocs:", totp.OtpAuthUri, StringComparison.Ordinal);

        // Ohne bestätigten zweiten Faktor ist niemand angemeldet und myDocs noch nicht eingerichtet.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative), Ct)).StatusCode);
        Assert.True((await client.GetFromJsonAsync<SetupStatusDto>("/api/setup/status", Ct))!.Required);

        var wrongTotp = await client.PostAsJsonAsync("/api/setup/finish", new SetupFinishRequest(code, "a@example.test", "000000"), Ct);
        Assert.Equal(AuthErrors.CodeInvalid, await TitleAsync(wrongTotp));

        var finish = await client.PostAsJsonAsync("/api/setup/finish", new SetupFinishRequest(code, "a@example.test", server.NextTotp(totp.Secret)), Ct);
        Assert.Equal(HttpStatusCode.OK, finish.StatusCode);
        Assert.Equal(10, (await finish.Content.ReadFromJsonAsync<RecoveryCodesDto>(Ct))!.Codes.Count);

        var me = (await client.GetFromJsonAsync<MeDto>("/api/auth/me", Ct))!;
        Assert.Equal(("a@example.test", "Anna", true), (me.Email, me.DisplayName, me.IsAdmin));
        Assert.False((await client.GetFromJsonAsync<SetupStatusDto>("/api/setup/status", Ct))!.Required);
        Assert.Null(server.SetupCode);

        var again = await client.PostAsJsonAsync("/api/setup/start", new SetupStartRequest(code, "b@example.test", "B", "ein langes Passwort"), Ct);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    internal static async Task<string?> TitleAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemTitle>(Ct))?.Title;

    internal sealed record ProblemTitle(string? Title);
}

public sealed class AuthTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LoginNeedsPasswordAndAOneTimeCode()
    {
        var admin = await server.EnsureAdminAsync();
        using var client = server.CreateApiClient();

        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(ServerFixture.AdminEmail, ServerFixture.AdminPassword), Ct);
        Assert.True((await login.Content.ReadFromJsonAsync<LoginResultDto>(Ct))!.TwoFactorRequired);
        // Nach dem Passwort allein ist noch niemand angemeldet.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative), Ct)).StatusCode);

        var code = server.NextTotp(admin.TotpSecret);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/login/2fa", new LoginTwoFactorRequest(code, false), Ct)).StatusCode);
        Assert.Equal(ServerFixture.AdminEmail, (await client.GetFromJsonAsync<MeDto>("/api/auth/me", Ct))!.Email);

        // Derselbe Code ein zweites Mal: abgelehnt, auch innerhalb seines Zeitfensters.
        using var other = server.CreateApiClient();
        await other.PostAsJsonAsync("/api/auth/login", new LoginRequest(ServerFixture.AdminEmail, ServerFixture.AdminPassword), Ct);
        var replay = await other.PostAsJsonAsync("/api/auth/login/2fa", new LoginTwoFactorRequest(code, false), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(new Uri("/api/auth/logout", UriKind.Relative), null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task ARecoveryCodeWorksOnlyOnce()
    {
        var admin = await server.EnsureAdminAsync();
        var recovery = admin.RecoveryCodes[^1];
        admin.RecoveryCodes.RemoveAt(admin.RecoveryCodes.Count - 1);

        using var client = server.CreateApiClient();
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(ServerFixture.AdminEmail, ServerFixture.AdminPassword), Ct);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/login/2fa", new LoginTwoFactorRequest(recovery, true), Ct)).StatusCode);
        Assert.True((await client.GetFromJsonAsync<MeDto>("/api/auth/me", Ct))!.RecoveryCodesLeft < 10);

        using var again = server.CreateApiClient();
        await again.PostAsJsonAsync("/api/auth/login", new LoginRequest(ServerFixture.AdminEmail, ServerFixture.AdminPassword), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, (await again.PostAsJsonAsync("/api/auth/login/2fa", new LoginTwoFactorRequest(recovery, true), Ct)).StatusCode);
    }

    [Fact]
    public async Task UnknownAddressesAndWrongPasswordsLookTheSame()
    {
        await server.EnsureAdminAsync();
        using var client = server.CreateApiClient();

        var unknown = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("niemand@example.test", "irgendein Passwort"), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(AuthErrors.Invalid, await SetupTests.TitleAsync(unknown));
    }

    [Fact]
    public async Task TheAccountLocksAfterFiveWrongPasswords()
    {
        await server.EnsureAdminAsync();
        using var scope = server.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
        var victim = new AppUser { Email = "lock@example.test", UserName = "lock@example.test", DisplayName = "Lock", EmailConfirmed = true };
        Assert.True((await users.CreateAsync(victim, "ein langes Passwort")).Succeeded);

        using var client = server.CreateApiClient();
        HttpResponseMessage? last = null;
        for (var i = 0; i < 5; i++)
        {
            last = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("lock@example.test", "falsches Passwort!"), Ct);
        }

        Assert.Equal(AuthErrors.Locked, await SetupTests.TitleAsync(last!));
        var correct = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("lock@example.test", "ein langes Passwort"), Ct);
        Assert.Equal(AuthErrors.Locked, await SetupTests.TitleAsync(correct));
    }

    [Fact]
    public async Task AnAccountWithoutSecondFactorCannotLogIn()
    {
        await server.EnsureAdminAsync();
        using (var scope = server.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<AppUser>>();
            await users.CreateAsync(new AppUser { Email = "no2fa@example.test", UserName = "no2fa@example.test", DisplayName = "X", EmailConfirmed = true }, "ein langes Passwort");
        }

        using var client = server.CreateApiClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("no2fa@example.test", "ein langes Passwort"), Ct);
        Assert.Equal(AuthErrors.TwoFactorMissing, await SetupTests.TitleAsync(login));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative), Ct)).StatusCode);
    }

    [Fact]
    public async Task TheAuthenticatorSecretIsEncryptedInTheDatabase()
    {
        var admin = await server.EnsureAdminAsync();
        using var scope = server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyDocsDbContext>();

        var stored = await db.UserTokens.Select(t => t.Value).ToListAsync(Ct);

        Assert.NotEmpty(stored);
        Assert.DoesNotContain(stored, v => v != null && (v.Contains(admin.TotpSecret, StringComparison.Ordinal) || v.Contains(admin.RecoveryCodes[0], StringComparison.Ordinal)));
    }
}
