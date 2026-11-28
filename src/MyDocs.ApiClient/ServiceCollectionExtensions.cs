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
        return services.AddMyDocsApi(_ =>
        {
            var http = new HttpClient { BaseAddress = baseAddress };
            http.DefaultRequestHeaders.Add(ApiHeaders.Csrf, ApiHeaders.CsrfValue);
            return http;
        });
    }

    /// <summary>Wie oben mit eigenem HttpClient, etwa mit dem Gerätetoken des Desktop-Clients.</summary>
    public static IServiceCollection AddMyDocsApi(this IServiceCollection services, Func<IServiceProvider, HttpClient> httpClient)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped(httpClient);
        services.AddScoped<AppInfoClient>();
        services.AddScoped<AuthClient>();
        services.AddScoped<AdminClient>();
        services.AddScoped<PersonClient>();
        services.AddScoped<ObjectClient>();
        services.AddScoped<DocumentClient>();
        services.AddScoped<SearchClient>();
        services.AddScoped<AuditClient>();
        services.AddScoped<TaskClient>();
        services.AddScoped<DashboardClient>();
        services.AddScoped<NotificationClient>();
        services.AddScoped<InboxClient>();
        services.AddScoped<DeviceClient>();
        return services;
    }
}
