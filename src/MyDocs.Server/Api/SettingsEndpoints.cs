using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Audit;
using MyDocs.Server.Infrastructure.Mail;
using MyDocs.Server.Infrastructure.Settings;
using MyDocs.Shared.Admin;
using MyDocs.Shared.Audit;
using MyDocs.Shared.Notifications;

namespace MyDocs.Server.Api;

/// <summary>
/// Einstellungen des Administrators (SMTP, ntfy, Adresse der Instanz, Dateigröße, Sicherung) und die eigenen
/// Benachrichtigungen jeder Person (47).
/// </summary>
internal static partial class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin/settings").RequireAuthorization(Roles.Admin);
        admin.MapGet("/", async (SettingsStore settings, CancellationToken ct) => TypedResults.Ok(await settings.GetForAdminAsync(ct)));
        admin.MapPut("/", SaveAsync);
        admin.MapPost("/test-mail", TestMailAsync);
        admin.MapPost("/test-ntfy", TestNtfyAsync);
        admin.MapPost("/test-imap", TestImapAsync);

        var own = app.MapGroup("/api/account/notifications").RequireAuthorization();
        own.MapGet("/", GetOwnAsync);
        own.MapPut("/", SaveOwnAsync);
        own.MapPost("/test", TestOwnAsync);
        return app;
    }

    private static async Task<IResult> SaveAsync(AdminSettingsDto request, SettingsStore settings, MyDocsDbContext db, AuditLog audit, CancellationToken ct)
    {
        var errors = AdminSettingsValidation.Validate(request, settings.FileSizeCeiling);
        if (errors.Count > 0)
        {
            var (field, code) = errors.First();
            return TypedResults.Problem(title: code, detail: field, statusCode: StatusCodes.Status400BadRequest);
        }

        audit.Add(AuditActions.SettingsChanged);
        await settings.SaveAsync(db, request, ct);
        return TypedResults.Ok(await settings.GetForAdminAsync(ct));
    }

    /// <summary>Testmail an die eigene Adresse, sofort und ohne Outbox, damit ein Fehler gleich zu sehen ist.</summary>
    private static async Task<IResult> TestMailAsync(ClaimsPrincipal principal, UserManager<AppUser> users, SettingsStore settings, IMailTransport transport,
        ILoggerFactory loggers, CancellationToken ct)
    {
        if (await users.GetUserAsync(principal) is not { Email: { } email } user)
        {
            return TypedResults.Unauthorized();
        }

        if (await SmtpConfig.FromAsync(settings, ct) is not { } config)
        {
            return ApiProblems.Of(SettingsErrors.MailNotConfigured);
        }

        try
        {
            var mail = MailTexts.Render(MailTemplate.Test, user.Language, new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = user.DisplayName });
            await transport.SendAsync(config, SmtpMailTransport.Build(config, email, mail), ct);
            return TypedResults.NoContent();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogTestFailed(loggers.CreateLogger(typeof(SettingsEndpoints)), "SMTP", exception.Message);
            return TypedResults.Problem(title: SettingsErrors.SendFailed, detail: Short(exception.Message), statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> TestNtfyAsync(NtfyTestRequest request, ClaimsPrincipal principal, UserManager<AppUser> users, SettingsStore settings,
        IPushSender push, ILoggerFactory loggers, CancellationToken ct)
    {
        if (await users.GetUserAsync(principal) is not { } user)
        {
            return TypedResults.Unauthorized();
        }

        if (!AdminSettingsValidation.IsTopic(request.Topic))
        {
            return ApiProblems.Of(SettingsErrors.Topic);
        }

        return await PushTestAsync(user, request.Topic, settings, push, loggers, ct);
    }

    /// <summary>Öffnet das Postfach einmal und meldet, wie viele Mails warten, oder den Fehler.</summary>
    private static async Task<IResult> TestImapAsync(SettingsStore settings, MyDocs.Server.Infrastructure.Inbox.MailIntakeService intake, ILoggerFactory loggers, CancellationToken ct)
    {
        if (!SettingsStore.IsImapConfigured(await settings.GetAsync(ct)))
        {
            return ApiProblems.Of(SettingsErrors.ImapNotConfigured);
        }

        try
        {
            return TypedResults.Ok(await intake.TestAsync(ct));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogTestFailed(loggers.CreateLogger(typeof(SettingsEndpoints)), "IMAP", exception.Message);
            return TypedResults.Problem(title: SettingsErrors.SendFailed, detail: Short(exception.Message), statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> GetOwnAsync(ClaimsPrincipal principal, UserManager<AppUser> users, SettingsStore settings, CancellationToken ct) =>
        await users.GetUserAsync(principal) is { } user ? TypedResults.Ok(await OwnDtoAsync(user, settings, ct)) : TypedResults.Unauthorized();

    private static async Task<IResult> SaveOwnAsync(NotificationSettingsRequest request, ClaimsPrincipal principal, UserManager<AppUser> users,
        SettingsStore settings, MyDocsDbContext db, CancellationToken ct)
    {
        if (await users.GetUserAsync(principal) is not { } user)
        {
            return TypedResults.Unauthorized();
        }

        var topic = string.IsNullOrWhiteSpace(request.NtfyTopic) ? null : request.NtfyTopic.Trim();
        if (!MailLanguages.All.Contains(request.Language) || request.DigestHour is < 0 or > 23 || (topic is not null && !AdminSettingsValidation.IsTopic(topic)))
        {
            return ApiProblems.Of(NotificationErrors.Invalid);
        }

        // Weitere Absenderadressen (49): gültig, höchstens fünf, keine Adresse eines anderen Kontos.
        if (request.MailSenders is { } senders)
        {
            var cleaned = senders.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim().ToLowerInvariant()).Distinct().ToList();
            if (cleaned.Count > MailIntakeLimits.MaxSenders || cleaned.Any(a => !AdminSettingsValidation.IsEmail(a)))
            {
                return ApiProblems.Of(NotificationErrors.InvalidSender);
            }

            var upper = cleaned.Select(a => a.ToUpperInvariant()).ToList();
            if (await db.Users.AnyAsync(u => u.Id != user.Id && (upper.Contains(u.NormalizedEmail!) || u.MailSenders.Any(m => cleaned.Contains(m))), ct))
            {
                return ApiProblems.Of(NotificationErrors.InvalidSender);
            }

            user.MailSenders = [.. cleaned.Where(a => !string.Equals(a, user.Email, StringComparison.OrdinalIgnoreCase))];
        }

        user.MailBodyAlways = request.MailBodyAlways;

        // Eine neue Uhrzeit gilt schon heute, wenn sie noch nicht vorbei ist.
        if (user.DigestHour != request.DigestHour)
        {
            user.LastDigestOn = null;
        }

        user.Language = request.Language;
        user.DigestHour = request.DigestHour;
        user.NtfyTopic = topic;
        await users.UpdateAsync(user);
        return TypedResults.Ok(await OwnDtoAsync(user, settings, ct));
    }

    /// <summary>Testmail über die Outbox und, wenn ein Thema hinterlegt ist, eine Push-Nachricht sofort.</summary>
    private static async Task<IResult> TestOwnAsync(ClaimsPrincipal principal, UserManager<AppUser> users, SettingsStore settings, MailOutbox outbox,
        MyDocsDbContext db, IPushSender push, ILoggerFactory loggers, CancellationToken ct)
    {
        if (await users.GetUserAsync(principal) is not { } user)
        {
            return TypedResults.Unauthorized();
        }

        var stored = await settings.GetAsync(ct);
        var mail = SettingsStore.IsMailConfigured(stored);
        var ntfy = SettingsStore.IsNtfyConfigured(stored) && user.NtfyTopic is not null;
        if (!mail && !ntfy)
        {
            return ApiProblems.Of(NotificationErrors.NothingConfigured);
        }

        if (mail)
        {
            outbox.Enqueue(user, MailTemplate.Test);
            await db.SaveChangesAsync(ct);
        }

        return ntfy ? await PushTestAsync(user, user.NtfyTopic!, settings, push, loggers, ct) : TypedResults.NoContent();
    }

    private static async Task<IResult> PushTestAsync(AppUser user, string topic, SettingsStore settings, IPushSender push, ILoggerFactory loggers, CancellationToken ct)
    {
        if (!SettingsStore.IsNtfyConfigured(await settings.GetAsync(ct)))
        {
            return ApiProblems.Of(SettingsErrors.NtfyNotConfigured);
        }

        try
        {
            await push.SendAsync(topic, MailTexts.Phrase("Push_test_title", user.Language) ?? "myDocs", MailTexts.Phrase("Push_test_body", user.Language) ?? "Test", null, ct);
            return TypedResults.NoContent();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            LogTestFailed(loggers.CreateLogger(typeof(SettingsEndpoints)), "ntfy", exception.Message);
            return TypedResults.Problem(title: SettingsErrors.SendFailed, detail: Short(exception.Message), statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<NotificationSettingsDto> OwnDtoAsync(AppUser user, SettingsStore settings, CancellationToken ct)
    {
        var stored = await settings.GetAsync(ct);
        return new NotificationSettingsDto(user.Language, user.DigestHour, user.NtfyTopic, SettingsStore.IsMailConfigured(stored), SettingsStore.IsNtfyConfigured(stored))
        {
            MailSenders = user.MailSenders,
            MailBodyAlways = user.MailBodyAlways,
            MailInAddress = SettingsStore.IsImapConfigured(stored) && AdminSettingsValidation.IsEmail(stored.Imap.Username) ? stored.Imap.Username : null,
        };
    }

    private static string Short(string message) => message.Length > 300 ? message[..300] : message;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Test über {Channel} fehlgeschlagen: {Error}")]
    private static partial void LogTestFailed(ILogger logger, string channel, string error);
}
