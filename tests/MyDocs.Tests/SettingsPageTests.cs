using System.Globalization;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MyDocs.ApiClient;
using MyDocs.Shared.Admin;
using MyDocs.Shared.Household;
using MyDocs.Shared.Notifications;
using MyDocs.UI;
using MyDocs.UI.Pages.Account;
using MyDocs.UI.Pages.Admin;

namespace MyDocs.Tests;

/// <summary>Einstellungen des Administrators und eigene Benachrichtigungen (M2.4).</summary>
public sealed class SettingsPageTests : BunitContext
{
    private readonly FakeApi _api = new();

    public SettingsPageTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMyDocsUi();
        Services.AddMyDocsApi(new Uri("http://localhost/"));
        Services.AddScoped(_ => new HttpClient(_api) { BaseAddress = new Uri("http://localhost/") });
        AddAuthorization().SetAuthorized("Admin").SetRoles(HouseholdRoles.Member, HouseholdRoles.Admin);
    }

    [Fact]
    public void SettingsAreCheckedBeforeSavingAndSecretsStayHidden()
    {
        var stored = new AdminSettingsDto
        {
            Smtp = new SmtpSettingsDto { Host = "smtp.example.test", FromAddress = "mydocs@example.test", HasPassword = true },
            FileSizeCeilingMegabytes = 100,
        };
        AdminSettingsDto? sent = null;
        _api.OnJson("GET /api/admin/settings", stored)
            .On("PUT /api/admin/settings", request =>
            {
                sent = request.Content!.ReadFromJsonAsync<AdminSettingsDto>().GetAwaiter().GetResult();
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = JsonContent.Create(sent! with { Smtp = sent.Smtp with { HasPassword = true } }) };
            });

        var page = Render<SettingsPage>();
        page.WaitForAssertion(() => Assert.Contains("Hinterlegt. Leer lassen, um es zu behalten.", page.Markup, StringComparison.Ordinal));

        page.Find("input[data-testid=from-address]").Change("keine Adresse");
        page.Find("[data-testid=settings-save]").Click();
        page.WaitForAssertion(() => Assert.Contains("Die E-Mail-Adresse ist ungültig.", page.Markup, StringComparison.Ordinal));
        Assert.DoesNotContain("PUT /api/admin/settings", _api.Calls);

        // Mehr als die Grenze der Installation lässt das Feld gar nicht zu.
        page.Find("input[data-testid=max-file-size]").Change("200");
        page.Find("input[data-testid=from-address]").Change("mydocs@example.test");
        page.Find("[data-testid=settings-save]").Click();
        page.WaitForAssertion(() => Assert.Contains("PUT /api/admin/settings", _api.Calls));
        Assert.Equal(100, sent!.MaxFileSizeMegabytes);

        page.Find("input[data-testid=max-file-size]").Change("50");
        page.Find("[data-testid=settings-save]").Click();
        page.WaitForAssertion(() => Assert.Equal(50, sent!.MaxFileSizeMegabytes));
        Assert.Null(sent.Smtp.Password);
        Assert.Equal("smtp.example.test", sent.Smtp.Host);
    }

    [Fact]
    public void WithoutMailAndNtfyTheNotificationPageSaysSo()
    {
        _api.OnJson("GET /api/account/notifications", new NotificationSettingsDto(MailLanguages.German, 7, null, false, false));

        var page = Render<NotificationsPage>();

        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid=nothing-configured]")));
    }

    [Fact]
    public void AnInvalidTopicIsNotSaved()
    {
        NotificationSettingsRequest? sent = null;
        _api.OnJson("GET /api/account/notifications", new NotificationSettingsDto(MailLanguages.German, 7, null, true, true))
            .On("PUT /api/account/notifications", request =>
            {
                sent = request.Content!.ReadFromJsonAsync<NotificationSettingsRequest>().GetAwaiter().GetResult();
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new NotificationSettingsDto(sent!.Language, sent.DigestHour, sent.NtfyTopic, true, true)),
                };
            });
        var page = Render<NotificationsPage>();
        page.WaitForElement("input[data-testid=ntfy-topic]").Change("mit leerzeichen");

        page.Find("[data-testid=notifications-save]").Click();
        Assert.DoesNotContain("PUT /api/account/notifications", _api.Calls);

        page.Find("input[data-testid=ntfy-topic]").Change("mydocs-anna");
        page.Find("[data-testid=notifications-save]").Click();
        page.WaitForAssertion(() => Assert.Equal(("mydocs-anna", (int?)7), (sent?.NtfyTopic, sent?.DigestHour)));
    }
}
