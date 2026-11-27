using Microsoft.Extensions.DependencyInjection;
using MyDocs.Shared;

namespace MyDocs.ApiClient;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// HttpClient zur API und alle typisierten Clients. Im Browser ist die Adresse die eigene Herkunft, im
    /// Desktop-Client die gespeicherte Adresse der Instanz.
    /// </summary>
    public static IServiceCollection AddMyDocsApi(this IServiceCollection services, Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped(_ =>
        {
            var http = new HttpClient { BaseAddress = baseAddress };
            http.DefaultRequestHeaders.Add(ApiHeaders.Csrf, ApiHeaders.CsrfValue);
            return http;
        });
        services.AddScoped<AppInfoClient>();
        services.AddScoped<AuthClient>();
        services.AddScoped<AdminClient>();
        services.AddScoped<PersonClient>();
        services.AddScoped<ObjectClient>();
        services.AddScoped<DocumentClient>();
        services.AddScoped<SearchClient>();
        return services;
    }
}
