using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;
using MyDocs.Server.Infrastructure.Inbox;
using MyDocs.Shared.Admin;
using MyDocs.Shared.Audit;
using MyDocs.Shared.Documents;
using MyDocs.Shared.Household;
using MyDocs.Shared.Notifications;

namespace MyDocs.Tests;

/// <summary>Mail-Eingang (M3.6): Absender, SPF/DKIM, Positivliste, Makros, Mailtext, doppelte Mails.</summary>
public sealed class MailIntakeTests(ServerFixture server) : IClassFixture<ServerFixture>, IAsyncLifetime
{
    private const string Mx = "mx.example.test";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static int _counter;
    private HttpClient _admin = null!;
    private HttpClient _bea = null!;
    private HttpClient _carl = null!;
    private string _beaEmail = null!;
    private string _guestEmail = null!;

    public async ValueTask InitializeAsync()
    {
        var n = Interlocked.Increment(ref _counter);
        _admin = await TestAccounts.AdminAsync(server);
        _beaEmail = $"mail-bea{n}@example.test";
        _guestEmail = $"mail-opa{n}@example.test";
        _bea = await TestAccounts.CreateAsync(server, _admin, _beaEmail, $"Bea {n}", HouseholdRoles.Member);
        _carl = await TestAccounts.CreateAsync(server, _admin, $"mail-carl{n}@example.test", $"Carl {n}", HouseholdRoles.Member);
        (await TestAccounts.CreateAsync(server, _admin, _guestEmail, $"Opa {n}", HouseholdRoles.Guest)).Dispose();
        await ConfigureAsync(trusted: null);
    }

    public ValueTask DisposeAsync()
    {
        _admin.Dispose();
        _bea.Dispose();
        _carl.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task ConfigureAsync(string? trusted)
    {
        var current = (await _admin.GetFromJsonAsync<AdminSettingsDto>("/api/admin/settings", Ct))!;
        var imap = new ImapSettingsDto { Host = "imap.example.test", Username = "dokumente@example.test", Password = "geheim", TrustedAuthServId = trusted };
        Assert.Equal(HttpStatusCode.OK, (await _admin.PutAsJsonAsync("/api/admin/settings", current with { Imap = imap }, Ct)).StatusCode);
    }

    private Task<int> RunAsync() => server.Services.GetRequiredService<MailIntakeService>().ProcessOnceAsync(Ct);

    private static MimeMessage Mail(string from, string subject, string? authResults, Action<BodyBuilder>? build = null)
    {
        var message = new MimeMessage { Subject = subject, MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId("example.test") };
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse("dokumente@example.test"));
        var body = new BodyBuilder { TextBody = "Hallo,\nanbei die Unterlagen.\nBea" };
        build?.Invoke(body);
        message.Body = body.ToMessageBody();
        if (authResults is not null)
        {
            message.Headers.Insert(0, HeaderId.AuthenticationResults, authResults);
        }

        return message;
    }

    private static string Pass(string from) => $"{Mx}; dkim=pass header.d={SenderCheck.Domain(from)}; spf=pass smtp.mailfrom={from}";

    private static async Task<List<InboxItemDto>> InboxAsync(HttpClient client) => (await client.GetFromJsonAsync<List<InboxItemDto>>("/api/inbox", Ct))!;

    [Fact]
    public async Task AttachmentsFromTheListGoToTheSendersPrivateInbox()
    {
        var message = Mail(_beaEmail, "Rechnung Strom", Pass(_beaEmail), body =>
        {
            body.Attachments.Add("Rechnung.pdf", "%PDF-1.4 Stromrechnung Dezember"u8.ToArray(), new ContentType("application", "pdf"));
            body.Attachments.Add("Programm.exe", "MZ"u8.ToArray(), new ContentType("application", "octet-stream"));
            body.Attachments.Add("Gefaelscht.pdf", "<html>"u8.ToArray(), new ContentType("application", "pdf"));
            var logo = body.LinkedResources.Add("logo.png", FakeWorker.PngSignature);
            logo.ContentId = "logo@example.test";
        });
        server.Mailbox.Add(message);

        Assert.Equal(1, await RunAsync());

        Assert.Contains(message, server.Mailbox.Folder(MailIntakeService.ProcessedFolder));
        var items = (await InboxAsync(_bea)).Where(i => i.SourceDetail == $"{_beaEmail} · Rechnung Strom").ToList();
        var item = Assert.Single(items);
        Assert.Equal(("Rechnung", DocumentSources.Mail, false, "application/pdf"), (item.Title, item.Source, item.Household, item.Current.ContentType));
        Assert.DoesNotContain(await InboxAsync(_carl), i => i.Id == item.Id);
    }

    [Fact]
    public async Task WithoutAttachmentsTheMailTextBecomesTheDocument()
    {
        var message = Mail(_beaEmail, "Zählerstand 12345", Pass(_beaEmail), body =>
        {
            body.TextBody = null;
            body.HtmlBody = "<html><head><style>p{}</style><script>alert(1)</script></head><body><p>Zählerstand: <b>12345</b> kWh</p><img src=\"https://tracker.example/x.gif\"></body></html>";
        });
        server.Mailbox.Add(message);

        await RunAsync();

        var item = Assert.Single(await InboxAsync(_bea), i => i.Title == "Zählerstand 12345");
        Assert.Equal(("Zählerstand 12345.txt", "text/plain"), (item.Current.FileName, item.Current.ContentType));
        var text = await _bea.GetStringAsync($"/api/documents/{item.Id}/content", Ct);
        Assert.Contains($"Von: {_beaEmail}", text, StringComparison.Ordinal);
        Assert.Contains("Zählerstand: 12345 kWh", text, StringComparison.Ordinal);
        Assert.DoesNotContain("alert", text, StringComparison.Ordinal);
        Assert.DoesNotContain("tracker", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnWishTheMailTextComesAlongWithTheAttachments()
    {
        var saved = await _bea.PutAsJsonAsync("/api/account/notifications", new NotificationSettingsRequest(MailLanguages.German, 7, null) { MailBodyAlways = true }, Ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        server.Mailbox.Add(Mail(_beaEmail, "Beides", Pass(_beaEmail), body => body.Attachments.Add("Brief.pdf", "%PDF-1.4 Brief"u8.ToArray(), new ContentType("application", "pdf"))));

        await RunAsync();

        Assert.Equal(2, (await InboxAsync(_bea)).Count(i => i.SourceDetail == $"{_beaEmail} · Beides"));
    }

    [Theory]
    [InlineData("spf=fail smtp.mailfrom={0}; dkim=none")]
    [InlineData("dkim=pass header.d=evil.test; spf=pass smtp.mailfrom=bounce@evil.test")]
    [InlineData(null)]
    public async Task SpoofedMailsAreRejectedAndTheSenderSeesWhy(string? results)
    {
        var message = Mail(_beaEmail, "Gefälscht", results is null ? null : $"{Mx}; " + string.Format(System.Globalization.CultureInfo.InvariantCulture, results, _beaEmail),
            body => body.Attachments.Add("Rechnung.pdf", "%PDF-1.4 Phishing"u8.ToArray(), new ContentType("application", "pdf")));
        server.Mailbox.Add(message);

        Assert.Equal(0, await RunAsync());

        Assert.Contains(message, server.Mailbox.Folder(MailIntakeService.RejectedFolder));
        Assert.DoesNotContain(await InboxAsync(_bea), i => i.SourceDetail?.Contains("Gefälscht", StringComparison.Ordinal) == true);
        var audit = (await _bea.GetFromJsonAsync<List<AuditEntryDto>>("/api/audit", Ct))!;
        Assert.Contains(audit, a => a.Action == AuditActions.MailRejected && a.Details!.StartsWith(_beaEmail, StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnknownSendersAndGuestsAreRejected()
    {
        var stranger = Mail("fremd@example.test", "Hallo", Pass("fremd@example.test"));
        var guest = Mail(_guestEmail, "Hallo", Pass(_guestEmail));
        server.Mailbox.Add(stranger);
        server.Mailbox.Add(guest);

        Assert.Equal(0, await RunAsync());

        Assert.Contains(stranger, server.Mailbox.Folder(MailIntakeService.RejectedFolder));
        Assert.Contains(guest, server.Mailbox.Folder(MailIntakeService.RejectedFolder));
    }

    [Fact]
    public async Task OnlyTheTrustedMailServerCountsWhenItIsSet()
    {
        await ConfigureAsync(Mx);
        try
        {
            // Oben ein gefälschter Header aus der Mail selbst, darunter das echte Ergebnis des eigenen Servers.
            var forged = Mail(_beaEmail, "Untergeschoben", $"{Mx}; spf=fail smtp.mailfrom={_beaEmail}");
            forged.Headers.Insert(0, HeaderId.AuthenticationResults, $"evil.test; dkim=pass header.d={SenderCheck.Domain(_beaEmail)}");
            var real = Mail(_beaEmail, "Echt", Pass(_beaEmail));
            real.Headers.Insert(0, HeaderId.AuthenticationResults, "evil.test; dkim=fail");
            server.Mailbox.Add(forged);
            server.Mailbox.Add(real);

            Assert.Equal(1, await RunAsync());

            Assert.Contains(forged, server.Mailbox.Folder(MailIntakeService.RejectedFolder));
            Assert.Contains(real, server.Mailbox.Folder(MailIntakeService.ProcessedFolder));
        }
        finally
        {
            await ConfigureAsync(trusted: null);
        }
    }

    [Fact]
    public async Task FurtherSenderAddressesBelongToOnePersonOnly()
    {
        var extra = $"privat-{Guid.NewGuid():N}@example.org";
        var request = new NotificationSettingsRequest(MailLanguages.German, 7, null) { MailSenders = [extra.ToUpperInvariant(), " "] };
        var saved = await _bea.PutAsJsonAsync("/api/account/notifications", request, Ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var dto = (await saved.Content.ReadFromJsonAsync<NotificationSettingsDto>(Ct))!;
        Assert.Equal([extra], dto.MailSenders);
        Assert.Equal("dokumente@example.test", dto.MailInAddress);

        Assert.Equal(HttpStatusCode.BadRequest, (await _carl.PutAsJsonAsync("/api/account/notifications", request, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _carl.PutAsJsonAsync("/api/account/notifications", request with { MailSenders = [_beaEmail] }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _carl.PutAsJsonAsync("/api/account/notifications", request with { MailSenders = ["keine-adresse"] }, Ct)).StatusCode);

        server.Mailbox.Add(Mail(extra, "Von privat", $"{Mx}; dmarc=pass header.from=example.org"));
        Assert.Equal(1, await RunAsync());
        Assert.Contains(await InboxAsync(_bea), i => i.Title == "Von privat");
    }

    [Fact]
    public async Task TheSameMailIsTakenOnlyOnce()
    {
        var message = Mail(_beaEmail, "Zweimal", Pass(_beaEmail));
        server.Mailbox.Add(message);
        await RunAsync();
        server.Mailbox.Add(message);

        Assert.Equal(1, await RunAsync());

        Assert.Single(await InboxAsync(_bea), i => i.Title == "Zweimal");
        Assert.Equal(2, server.Mailbox.Folder(MailIntakeService.ProcessedFolder).Count(m => ReferenceEquals(m, message)));
    }

    [Fact]
    public async Task OfficeMacrosAreFlaggedNotRemoved()
    {
        server.Mailbox.Add(Mail(_beaEmail, "Tabelle", Pass(_beaEmail), body =>
        {
            body.Attachments.Add("Haushalt.xlsx", Zip("[Content_Types].xml", "xl/vbaProject.bin"), new ContentType("application", "vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
            body.Attachments.Add("Brief.docx", Zip("[Content_Types].xml", "word/document.xml"), new ContentType("application", "vnd.openxmlformats-officedocument.wordprocessingml.document"));
        }));

        await RunAsync();

        var items = (await InboxAsync(_bea)).Where(i => i.SourceDetail == $"{_beaEmail} · Tabelle").ToDictionary(i => i.Title);
        Assert.True(items["Haushalt"].HasMacros);
        Assert.False(items["Brief"].HasMacros);
        var document = (await _bea.GetFromJsonAsync<DocumentDto>($"/api/documents/{items["Haushalt"].Id}", Ct))!;
        Assert.True(document.HasMacros);
    }

    [Fact]
    public async Task TheImapTestReportsWaitingMails()
    {
        var response = await _admin.PostAsync(new Uri("/api/admin/settings/test-imap", UriKind.Relative), null, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("geheim", server.Mailbox.LastConfig!.Password);
        var settings = (await _admin.GetFromJsonAsync<AdminSettingsDto>("/api/admin/settings", Ct))!;
        Assert.Null(settings.Imap.Password);
        Assert.True(settings.Imap.HasPassword);
    }

    [Fact]
    public void HtmlBecomesPlainTextAndDomainsAlign()
    {
        Assert.Equal("Hallo Welt\n\nZweiter Absatz & mehr", MailText.HtmlToText("<p>Hallo&nbsp;<b>Welt</b></p><p></p><p></p><div>Zweiter Absatz &amp; mehr</div><script>x()</script>"));
        Assert.True(SenderCheck.Aligned("mail.example.de", "example.de"));
        Assert.True(SenderCheck.Aligned("example.de", "mail.example.de"));
        Assert.False(SenderCheck.Aligned("example.de", "example.de.evil.test"));
        Assert.False(SenderCheck.Aligned("example.de", "badexample.de"));
    }

    private static byte[] Zip(params string[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in entries)
            {
                using var stream = zip.CreateEntry(entry).Open();
                stream.Write(Encoding.UTF8.GetBytes("x"));
            }
        }

        return buffer.ToArray();
    }
}
