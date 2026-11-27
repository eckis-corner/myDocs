using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using MyCommon.Ui;
using MyDocs.UI.Services;

namespace MyDocs.UI;

public static class ServiceCollectionExtensions
{
    /// <summary>Dienste der Oberfläche, gleich für Browser und Desktop-Client. Die API richtet der Host ein.</summary>
    public static IServiceCollection AddMyDocsUi(this IServiceCollection services)
    {
        services.AddMudServices();
        services.AddLocalization();
        services.AddMyCommonUi();
        services.AddScoped<CultureService>();
        return services;
    }
}
