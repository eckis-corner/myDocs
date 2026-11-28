using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MyDocs.ApiClient;
using MyDocs.Desktop;
using MyDocs.Desktop.Core;
using MyDocs.Desktop.Core.Auth;
using MyDocs.Desktop.Core.Secrets;
using MyDocs.UI;
using MyDocs.UI.Services;
using Photino.Blazor;

// Desktop-Client (36, M4): dieselbe Oberfläche wie im Browser, dazu Anmeldung über den Systembrowser und Gerätetoken.
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var builder = PhotinoBlazorAppBuilder.CreateDefault(args);
        var paths = DesktopPaths.ForCurrentUser();
        DesktopPaths.EnsurePrivateDirectory(paths.Root);

        builder.Services.AddLogging();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(paths);
        builder.Services.AddSingleton<DesktopSettingsStore>();
        builder.Services.AddSingleton(SecretStores.ForCurrentPlatform());
        builder.Services.AddSingleton<DeviceSession>();
        builder.Services.AddSingleton(services => new MyDocs.Desktop.Core.Checkout.CheckoutEngine(paths,
            () => services.GetRequiredService<DeviceSession>().CreateApiClient(), TimeProvider.System));
        builder.Services.AddSingleton<DesktopPlatform>();
        builder.Services.AddSingleton<IDesktopPlatform>(services => services.GetRequiredService<DesktopPlatform>());
        builder.Services.AddMyDocsUi();
        // Der HttpClient entsteht erst, wenn die Oberfläche nach dem Verbinden ihn braucht; dann steht die Adresse fest.
        builder.Services.AddMyDocsApi(services => services.GetRequiredService<DeviceSession>().CreateApiClient());
        builder.RootComponents.Add<DesktopRoot>("#app");

        var app = builder.Build();
        var platform = app.Services.GetRequiredService<DesktopPlatform>();
        platform.CleanOpenFolder();
        app.Services.GetRequiredService<DeviceSession>().RestoreAsync(CancellationToken.None).GetAwaiter().GetResult();

        app.MainWindow
            .SetTitle("myDocs")
            .SetSize(1280, 860)
            .SetUseOsDefaultSize(false)
            .Center()
            .RegisterCustomSchemeHandler(DesktopPlatform.Scheme, (object sender, string scheme, string url, out string contentType) => platform.Resource(url, out contentType));
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "wwwroot", "icon.png")))
        {
            app.MainWindow.SetIconFile(Path.Combine(AppContext.BaseDirectory, "wwwroot", "icon.png"));
        }

        app.MainWindow.SetNotificationsEnabled(true);
        platform.Notify = (title, message) => app.MainWindow.SendNotification(title, message);
        using var background = new CancellationTokenSource();
        _ = Task.Run(() => platform.RunBackgroundAsync(background.Token));

        // Kein Tray-Symbol (Photino kennt keins, 157): Wer mit ausgecheckten Dateien schließt, wird gefragt.
        app.MainWindow.RegisterWindowClosingHandler((sender, _) =>
        {
            var open = platform.Checkouts.Count;
            if (open == 0)
            {
                return false;
            }

            var texts = app.Services.GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizer<MyDocs.UI.Resources.SharedResource>>();
            var answer = app.MainWindow.ShowMessage("myDocs", texts["DesktopCloseWithCheckouts", open], Photino.NET.PhotinoDialogButtons.YesNo, Photino.NET.PhotinoDialogIcon.Question);
            return answer != Photino.NET.PhotinoDialogResult.Yes;
        });

        AppDomain.CurrentDomain.UnhandledException += (_, error) =>
            app.MainWindow.ShowMessage("myDocs", error.ExceptionObject.ToString() ?? "Fehler", Photino.NET.PhotinoDialogButtons.Ok, Photino.NET.PhotinoDialogIcon.Error);
        app.Run();
        background.Cancel();
    }
}
