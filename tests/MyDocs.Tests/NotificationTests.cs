using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Mail;
using MyDocs.Server.Infrastructure.Notifications;
using MyDocs.Server.Infrastructure.Tasks;
using MyDocs.Shared;
using MyDocs.Shared.Admin;
using MyDocs.Shared.Household;
using MyDocs.Shared.Notifications;
using MyDocs.Shared.Objects;
using MyDocs.Shared.Tasks;

namespace MyDocs.Tests;

/// <summary>Einstellungen, Mail-Outbox, ntfy, tägliche Zusammenfassung und sofortige Meldung in Stufe 3 (M2.4).</summary>
public sealed class NotificationTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    private const string NtfyUrl = "https://ntfy.example.test";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateOnly Today => DateOnly.FromDateTime(server.Clock.GetUtcNow().UtcDateTime);

    private static AdminSettingsDto Configured(string? password = "geheim") => new()
    {
        PublicUrl = "https://mydocs.example.test/",
        Smtp = new SmtpSettingsDto { Host = "smtp.example.test", Port = 587, Username = "mydocs", Password = password, FromAddress = "mydocs@example.test" },
        Ntfy = new NtfySettingsDto { ServerUrl = NtfyUrl },
    };

    private static async Task ConfigureAsync(HttpClient admin)
    {
        var response = await admin.PutAsJsonAsync("/api/admin/settings", Configured(), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<int> ProcessOutboxAsync() => await server.Services.GetRequiredService<MailOutboxService>().ProcessOnceAsync(Ct);

    private async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = server.Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    private static async Task SetOwnAsync(HttpClient client, int? hour, string? topic = null)
    {
        var response = await client.PutAsJsonAsync("/api/account/notifications", new NotificationSettingsRequest(MailLanguages.German, hour, topic), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<Guid> ObjectAsync(HttpClient client, string title, Guid? owner, Guid responsible)
    {
        var response = await client.PostAsJsonAsync("/api/objects", new ObjectSaveRequest("contract",
            new ObjectData { Title = title, Fields = new() { ["contractKind"] = "gym", ["provider"] = "FitX", ["cost"] = "30", ["frequency"] = "monthly" } }, owner, responsible), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ObjectDto>(Ct))!.Id;
    }

    private static async Task TaskAsync(HttpClient client, string title, Guid responsible, DateOnly due, Guid? objectId = null)
    {
        var response = await client.PostAsJsonAsync("/api/tasks", new TaskSaveRequest(title, null, responsible, due, TaskPriorities.Normal, objectId is { } id ? [id] : []), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SettingsKeepSecretsOnTheServer()
    {
        using var admin = await TestAccounts.AdminAsync(server);
        var defaults = (await admin.GetFromJsonAsync<AdminSettingsDto>("/api/admin/settings", Ct))!;
        Assert.Equal((100, 100, 3), (defaults.MaxFileSizeMegabytes, defaults.FileSizeCeilingMegabytes, defaults.Backup.Hour));

        var saved = await admin.PutAsJsonAsync("/api/admin/settings", Configured() with { MaxFileSizeMegabytes = 50, Backup = new BackupSettingsDto { Hour = 4 } }, Ct);
        var dto = (await saved.Content.ReadFromJsonAsync<AdminSettingsDto>(Ct))!;
        Assert.Null(dto.Smtp.Password);
        Assert.True(dto.Smtp.HasPassword);
        Assert.Equal("https://mydocs.example.test", dto.PublicUrl);
        var raw = await InScopeAsync(s => s.GetRequiredService<MyDocsDbContext>().SystemSettings.SingleAsync(x => x.Key == "admin:settings", Ct));
        Assert.DoesNotContain("geheim", raw.Value, StringComparison.Ordinal);

        // Ohne Passwort bleibt das hinterlegte.
        var kept = (await (await admin.PutAsJsonAsync("/api/admin/settings", Configured(password: null) with { MaxFileSizeMegabytes = 50 }, Ct)).Content.ReadFromJsonAsync<AdminSettingsDto>(Ct))!;
        Assert.True(kept.Smtp.HasPassword);
        Assert.Equal(50, (await admin.GetFromJsonAsync<AppInfoDto>("/api/info", Ct))!.MaxFileSizeMegabytes);

        var tooLarge = await admin.PutAsJsonAsync("/api/admin/settings", Configured() with { MaxFileSizeMegabytes = 101 }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, tooLarge.StatusCode);
        Assert.Contains(SettingsErrors.Range, await tooLarge.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        using var member = await TestAccounts.CreateAsync(server, admin, $"set-{Guid.NewGuid():N}@example.test", "Bea", HouseholdRoles.Member);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/admin/settings", Ct)).StatusCode);
        await admin.PutAsJsonAsync("/api/admin/settings", Configured() with { MaxFileSizeMegabytes = 100 }, Ct);
    }

    [Fact]
    public async Task TestMailAndActivationLinkGoOutByMail()
    {
        using var admin = await TestAccounts.AdminAsync(server);
        await ConfigureAsync(admin);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync("/api/admin/settings/test-mail", null, Ct)).StatusCode);
        Assert.Contains(server.Mail.Sent, m => m.To == ServerFixture.AdminEmail && m.Subject == "myDocs: Testmail");

        var email = $"mail-{Guid.NewGuid():N}@example.test";
        var created = await admin.PostAsJsonAsync("/api/admin/users", new CreateUserRequest(email, "Carla", HouseholdRoles.Guest, false), Ct);
        var link = (await created.Content.ReadFromJsonAsync<ActivationLinkDto>(Ct))!;
        Assert.True(link.Mailed);
        await ProcessOutboxAsync();
        var mail = Assert.Single(server.Mail.Sent, m => m.To == email);
        Assert.Contains($"https://mydocs.example.test/activate#{link.Token}", mail.Text, StringComparison.Ordinal);
        Assert.Contains("<a href=", mail.Html, StringComparison.Ordinal);
        // Nach dem Versand liegt der Link nicht mehr in der Outbox.
        var entries = await InScopeAsync(s => s.GetRequiredService<MyDocsDbContext>().MailOutbox.Where(m => m.Recipient == email).ToListAsync(Ct));
        Assert.All(entries, e => Assert.Null(e.Parameters));
    }

    [Fact]
    public async Task TheDigestComesOnceADayWithOwnAndEscalatedTasks()
    {
        using var admin = await TestAccounts.AdminAsync(server);
        await ConfigureAsync(admin);
        var email = $"digest-{Guid.NewGuid():N}@example.test";
        using var member = await TestAccounts.CreateAsync(server, admin, email, "Bea", HouseholdRoles.Member);
        var me = (await TestAccounts.MeAsync(admin)).PersonId!.Value;
        var bea = (await TestAccounts.MeAsync(member)).PersonId!.Value;
        await SetOwnAsync(admin, hour: null);
        await SetOwnAsync(member, hour: 0, topic: "mydocs-bea-test");

        var gym = await ObjectAsync(admin, "Fitnessstudio", null, bea);
        await TaskAsync(member, "Vertrag kündigen", bea, Today.AddDays(-1), gym);
        await TaskAsync(member, "Rechnung ablegen", bea, Today.AddDays(3));
        await TaskAsync(admin, "Steuer vorbereiten", me, Today.AddDays(-8));
        await TaskAsync(admin, "Erst im nächsten Monat", me, Today.AddDays(30));
        await InScopeAsync(s => s.GetRequiredService<ReviewEngine>().RunAsync(Ct));

        Assert.True(await InScopeAsync(s => s.GetRequiredService<Notifier>().SendDigestsAsync(Ct)) >= 1);
        await ProcessOutboxAsync();
        static bool IsDigest(SentMail m) => m.Subject.Contains("stehen an", StringComparison.Ordinal);
        var mail = Assert.Single(server.Mail.Sent, m => m.To == email && IsDigest(m));
        Assert.Equal("myDocs: 3 Vorgänge stehen an", mail.Subject);
        Assert.Contains("Fitnessstudio: Vertrag kündigen", mail.Text, StringComparison.Ordinal);
        Assert.Contains("Rechnung ablegen", mail.Text, StringComparison.Ordinal);
        Assert.Contains("Bei anderen seit über einer Woche überfällig:", mail.Text, StringComparison.Ordinal);
        Assert.Contains("Steuer vorbereiten", mail.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Erst im nächsten Monat", mail.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(server.Mail.Sent, m => m.To == ServerFixture.AdminEmail && IsDigest(m));

        // Push nur mit Zahlen, ohne Titel.
        var push = Assert.Single(server.Push.Sent, p => p.Topic == "mydocs-bea-test");
        Assert.Equal("myDocs: 3 Vorgänge", push.Title);
        Assert.Equal("2 überfällig, 1 in den nächsten 7 Tagen", push.Message);
        Assert.Equal("https://mydocs.example.test/tasks", push.Click);

        // Ein zweiter Lauf am selben Tag schickt nichts.
        await InScopeAsync(s => s.GetRequiredService<Notifier>().SendDigestsAsync(Ct));
        await ProcessOutboxAsync();
        Assert.Single(server.Mail.Sent, m => m.To == email && IsDigest(m));
    }

    [Fact]
    public async Task StageThreeGoesOutAtOnceButPrivateEntriesStayPrivate()
    {
        using var admin = await TestAccounts.AdminAsync(server);
        await ConfigureAsync(admin);
        var email = $"urgent-{Guid.NewGuid():N}@example.test";
        using var member = await TestAccounts.CreateAsync(server, admin, email, "Bea", HouseholdRoles.Member);
        var me = (await TestAccounts.MeAsync(admin)).PersonId!.Value;
        var bea = (await TestAccounts.MeAsync(member)).PersonId!.Value;

        var shared = await ObjectAsync(admin, "Gemeinsames Abo", null, me);
        var secret = await ObjectAsync(member, "Geheimes Abo", bea, bea);
        await TaskAsync(admin, "Haushaltsfrist", me, Today.AddDays(-15), shared);
        await TaskAsync(member, "Private Frist", bea, Today.AddDays(-15), secret);
        await InScopeAsync(s => s.GetRequiredService<ReviewEngine>().RunAsync(Ct));
        await ProcessOutboxAsync();

        var toBea = Assert.Single(server.Mail.Sent, m => m.To == email && m.Subject.Contains("14 Tagen", StringComparison.Ordinal));
        Assert.Contains("Gemeinsames Abo: Haushaltsfrist", toBea.Text, StringComparison.Ordinal);
        Assert.Contains("Geheimes Abo: Private Frist", toBea.Text, StringComparison.Ordinal);
        var toAdmin = server.Mail.Sent.Where(m => m.To == ServerFixture.AdminEmail && m.Text.Contains("Haushaltsfrist", StringComparison.Ordinal)).ToList();
        Assert.Single(toAdmin);
        Assert.DoesNotContain("Geheimes Abo", toAdmin[0].Text, StringComparison.Ordinal);

        // Ein zweiter Lauf meldet nichts erneut.
        var before = server.Mail.Sent.Count;
        await InScopeAsync(s => s.GetRequiredService<ReviewEngine>().RunAsync(Ct));
        await ProcessOutboxAsync();
        Assert.Equal(before, server.Mail.Sent.Count);
    }

    [Fact]
    public async Task OwnSettingsAreChecked()
    {
        using var admin = await TestAccounts.AdminAsync(server);
        var bad = await admin.PutAsJsonAsync("/api/account/notifications", new NotificationSettingsRequest("fr", 7, null), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var badTopic = await admin.PutAsJsonAsync("/api/account/notifications", new NotificationSettingsRequest(MailLanguages.English, 25, "mit leerzeichen"), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, badTopic.StatusCode);
        await SetOwnAsync(admin, 6, "mydocs-admin-test");
        var own = (await admin.GetFromJsonAsync<NotificationSettingsDto>("/api/account/notifications", Ct))!;
        Assert.Equal((6, "mydocs-admin-test"), (own.DigestHour, own.NtfyTopic));
    }
}
