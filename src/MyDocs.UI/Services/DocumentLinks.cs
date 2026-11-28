using Microsoft.Extensions.DependencyInjection;
using MyDocs.ApiClient;

namespace MyDocs.UI.Services;

/// <summary>
/// Adressen von Inhalten: im Browser die API mit dem Sitzungs-Cookie, im Desktop-Client das Schema des Hosts mit dem
/// Gerätetoken. Downloads sind im Browser Links, im Desktop ein Öffnen mit der Standard-App.
/// </summary>
public sealed class DocumentLinks(IServiceProvider services)
{
    private IDesktopPlatform? Desktop => services.GetService<IDesktopPlatform>();

    public bool IsDesktop => Desktop is not null;

    public string Preview(Guid id, int? version = null) => Wrap(DocumentClient.PreviewUrl(id, version));

    public string Thumbnail(Guid id, int? version = null) => Wrap(DocumentClient.ThumbnailUrl(id, version));

    /// <summary>Link zum Herunterladen; null im Desktop, dort <see cref="OpenAsync"/>.</summary>
    public string? Download(Guid id, int? version = null) => Desktop is null ? DocumentClient.ContentUrl(id, version, download: true) : null;

    public Task OpenAsync(Guid id, int? version, string fileName) => Desktop?.OpenAsync(id, version, fileName) ?? Task.CompletedTask;

    private string Wrap(string path) => Desktop?.ResourceUrl(path) ?? path;
}
