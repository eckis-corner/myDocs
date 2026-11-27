using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace MyDocs.Tests;

/// <summary>Wer was ohne Anmeldung erreicht. Jeder neue Endpunkt ohne Schutz lässt den Test scheitern.</summary>
public sealed class SecurityTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    /// <summary>Bewusst ohne Anmeldung erreichbar; jeder Eintrag hier ist eine Entscheidung.</summary>
    private static readonly HashSet<string> PublicEndpoints = new(StringComparer.Ordinal)
    {
        "GET /api/info",
        // Einrichtung (nur solange myDocs nicht eingerichtet ist, mit Einrichtungscode)
        "GET /api/setup/status", "POST /api/setup/start", "POST /api/setup/finish",
        // Anmeldung, zweiter Faktor (mit dem Zwischen-Cookie von Identity), Abmelden
        "POST /api/auth/login", "POST /api/auth/login/2fa", "POST /api/auth/logout",
        // Passkey als zweiter Faktor, nur mit dem Zwischen-Cookie nach dem Passwort (M1.2b)
        "POST /api/auth/login/passkey/options", "POST /api/auth/login/passkey",
        // Aktivierung über den Link des Administrators (Einmal-Token im #-Teil der Adresse)
        "POST /api/activation/info", "POST /api/activation/start", "POST /api/activation/finish",
    };

    [Fact]
    public void EveryApiEndpointRequiresALoginUnlessItIsPublicByDesign()
    {
        var anonymous = ApiEndpoints()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null || !e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any())
            .Select(Describe).Order(StringComparer.Ordinal).ToList();
        var unexpected = anonymous.Except(PublicEndpoints).ToList();
        var missing = PublicEndpoints.Except(anonymous).ToList();
        Assert.True(unexpected.Count == 0 && missing.Count == 0,
            $"Ohne Anmeldung erreichbar, aber nicht vorgesehen:\n{string.Join("\n", unexpected)}\nVorgesehen, aber nicht gefunden:\n{string.Join("\n", missing)}");
    }

    [Fact]
    public void AdminEndpointsRequireTheAdminRole()
    {
        var admin = ApiEndpoints().Where(e => e.RoutePattern.RawText!.StartsWith("/api/admin", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(admin);
        Assert.All(admin, e => Assert.Contains(e.Metadata.GetOrderedMetadata<IAuthorizeData>(), a => a.Policy == MyDocs.Server.Data.Roles.Admin));
    }

    [Fact]
    public async Task WritingWithoutTheCsrfHeaderIsRejected()
    {
        using var client = server.CreateClient();

        var response = await client.PostAsync(new Uri("/api/auth/logout", UriKind.Relative), null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpointsAnswerWith401InsteadOfARedirect()
    {
        using var client = server.CreateApiClient();

        var response = await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private IEnumerable<RouteEndpoint> ApiEndpoints() =>
        server.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api", StringComparison.Ordinal) == true);

    private static string Describe(RouteEndpoint endpoint) =>
        $"{string.Join(",", endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["*"])} {endpoint.RoutePattern.RawText}";
}
