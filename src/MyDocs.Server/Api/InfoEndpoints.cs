using System.Reflection;
using MyDocs.Shared;

namespace MyDocs.Server.Api;

internal static class InfoEndpoints
{
    public static IEndpointRouteBuilder MapInfoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/info", (Microsoft.Extensions.Options.IOptions<MyDocs.Server.Infrastructure.MyDocsOptions> options) =>
                TypedResults.Ok(new AppInfoDto(AppVersion, options.Value.MaxFileSizeMegabytes)))
            .WithName("GetAppInfo");
        return app;
    }

    internal static string AppVersion { get; } =
        typeof(InfoEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";
}
