using System.Net;
using System.Net.Http.Json;
using MyDocs.Shared;

namespace MyDocs.Tests;

public sealed class ServerTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    [Fact]
    public async Task ReadinessReportsHealthyDatabase()
    {
        using var client = server.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task InfoReturnsVersion()
    {
        using var client = server.CreateClient();

        var info = await client.GetFromJsonAsync<AppInfoDto>("/api/info", TestContext.Current.CancellationToken);

        Assert.NotNull(info);
        Assert.False(string.IsNullOrWhiteSpace(info.Version));
    }

    [Fact]
    public async Task PagesCarryStrictSecurityHeaders()
    {
        using var client = server.CreateClient();

        var response = await client.GetAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);

        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("script-src 'self' 'wasm-unsafe-eval'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.False(response.Headers.Contains("Server"));
    }

    [Fact]
    public async Task PageIsOnlyTheShell()
    {
        using var client = server.CreateClient();

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.DoesNotContain("Willkommen bei myDocs", html, StringComparison.Ordinal);
        Assert.Contains("_framework/blazor.web", html, StringComparison.Ordinal);
        Assert.Contains("_content/MyDocs.UI/css/app", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/_content/MyCommon.Ui/fonts/TitilliumWeb-Regular.ttf")]
    [InlineData("/_content/MyDocs.UI/img/icon.svg")]
    [InlineData("/_content/MyDocs.UI/js/mydocs.js")]
    public async Task SharedAssetsAreServed(string path)
    {
        using var client = server.CreateClient();

        var response = await client.GetAsync(new Uri(path, UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
