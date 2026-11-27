using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using MyDocs.ApiClient;
using MyDocs.Shared.Auth;

namespace MyDocs.UI.Services;

/// <summary>
/// Anmeldestatus der Oberfläche. Quelle ist der Server (<c>/api/auth/me</c>); der Browser kennt nur das Cookie,
/// das er nicht lesen kann. Rollen dienen nur der Anzeige, geprüft wird auf dem Server.
/// </summary>
public sealed class SessionService(AuthClient auth) : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));
    private Task<AuthenticationState>? _state;

    public MeDto? Me { get; private set; }

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => _state ??= LoadAsync();

    /// <summary>Nach Anmeldung oder Abmeldung: Status neu vom Server holen.</summary>
    public async Task RefreshAsync()
    {
        _state = LoadAsync();
        NotifyAuthenticationStateChanged(_state);
        await _state;
    }

    public async Task LogoutAsync()
    {
        try
        {
            await auth.LogoutAsync();
        }
        finally
        {
            Me = null;
            _state = Task.FromResult(Anonymous);
            NotifyAuthenticationStateChanged(_state);
        }
    }

    private async Task<AuthenticationState> LoadAsync()
    {
        try
        {
            Me = await auth.GetMeAsync();
        }
        catch (HttpRequestException)
        {
            Me = null;
        }

        if (Me is null)
        {
            return Anonymous;
        }

        List<Claim> claims = [new(ClaimTypes.NameIdentifier, Me.Id.ToString()), new(ClaimTypes.Name, Me.DisplayName), new(ClaimTypes.Email, Me.Email)];
        claims.Add(new(ClaimTypes.Role, Me.Role));
        if (Me.IsAdmin)
        {
            claims.Add(new(ClaimTypes.Role, MyDocs.Shared.Household.HouseholdRoles.Admin));
        }

        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, "mydocs")));
    }
}
