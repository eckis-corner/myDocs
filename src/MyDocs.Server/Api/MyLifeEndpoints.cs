using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure;
using MyDocs.Server.Infrastructure.Audit;
using MyDocs.Server.Infrastructure.Files;
using MyDocs.Server.Infrastructure.MyLife;
using MyDocs.Server.Infrastructure.Objects;
using MyDocs.Shared.Audit;
using MyDocs.Shared.Documents;
using MyDocs.Shared.MyLife;
using MyDocs.Shared.Objects;
using MyDocs.Shared.Tasks;

namespace MyDocs.Server.Api;

/// <summary>
/// Brücke zu myLife (56–60, 162–166). Im eigenen Konto: Verbindung anlegen und widerrufen. An Einträgen und
/// Dokumenten: die Markierungen. Unter <c>/api/mylife</c>: was myLife mit dem lesenden Token abholt, nur vom Origin
/// aus <c>MyDocs:MyLifeUrl</c> (CORS, ohne Cookies). Das Token öffnet keinen anderen Endpunkt.
/// </summary>
internal static partial class MyLifeEndpoints
{
    public const string CorsPolicy = "MyLife";

    public static IEndpointRouteBuilder MapMyLifeEndpoints(this IEndpointRouteBuilder app)
    {
        var account = app.MapGroup("/api/account/mylife").RequireAuthorization();
        account.MapGet("", StatusAsync);
        account.MapPost("", ConnectAsync);
        account.MapDelete("", DisconnectAsync);

        app.MapPut("/api/objects/{id:guid}/mylife", ObjectFlagAsync).RequireAuthorization();
        app.MapPut("/api/documents/{id:guid}/mylife", DocumentFlagAsync).RequireAuthorization();

        // Anmeldung über das lesende Token, nicht über Cookie oder Gerät: deshalb hier selbst geprüft.
        var bridge = app.MapGroup("/api/mylife").AllowAnonymous().RequireCors(CorsPolicy);
        bridge.MapGet("/export", ExportAsync);
        bridge.MapGet("/documents/{id:guid}/content", ContentAsync);
        bridge.MapPost("/synced", SyncedAsync);
        return app;
    }

    private static async Task<IResult> StatusAsync(ClaimsPrincipal principal, ObjectAccess access, MyDocsDbContext db, MyLifeBridge bridge,
        IOptions<MyDocsOptions> options, HttpRequest request, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        var link = await db.MyLifeLinks.AsNoTracking().FirstOrDefaultAsync(l => l.UserId == caller.UserId, ct);
        if (caller.PersonId is not { } personId || await db.Persons.FirstOrDefaultAsync(p => p.Id == personId, ct) is not { } person)
        {
            return TypedResults.Ok(new MyLifeLinkDto(link is not null, link?.CreatedAt, link?.LastUsedAt, link?.LastSyncedAt, false, null, [], MyLifeUrl(options), false));
        }

        var export = await bridge.BuildAsync(person, InstanceUrl(request), ct);
        var objects = export.Objects.Select(o => new MyLifeObjectDto(o.Id, o.Type, o.Title, o.Documents.Count)).OrderBy(o => o.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        var pending = link is not null && link.SyncedFingerprint != export.Fingerprint;
        return TypedResults.Ok(new MyLifeLinkDto(link is not null, link?.CreatedAt, link?.LastUsedAt, link?.LastSyncedAt, pending,
            pending ? link!.PendingSince ?? link.LastSyncedAt ?? link.CreatedAt : null, objects, MyLifeUrl(options), true));
    }

    /// <summary>Neues Token; ein bestehendes gilt danach nicht mehr. Es wird genau einmal gezeigt.</summary>
    private static async Task<IResult> ConnectAsync(ClaimsPrincipal principal, ObjectAccess access, MyDocsDbContext db, AuditLog audit,
        IOptions<MyDocsOptions> options, HttpRequest request, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (caller.PersonId is null)
        {
            return ApiProblems.Of(MyLifeErrors.NoPerson);
        }

        var token = MyLifeBridge.NewToken();
        var link = await db.MyLifeLinks.FirstOrDefaultAsync(l => l.UserId == caller.UserId, ct);
        if (link is null)
        {
            link = new MyLifeLink { UserId = caller.UserId, CreatedAt = access.Now };
            db.MyLifeLinks.Add(link);
        }
        else
        {
            // Neues Token, derselbe Abgleich: Der quittierte Stand bleibt gültig.
            link.CreatedAt = access.Now;
            link.LastUsedAt = null;
        }

        link.TokenHash = MyLifeBridge.Hash(token);
        audit.Add(AuditActions.MyLifeConnected, caller.UserId, caller.PersonId);
        await db.SaveChangesAsync(ct);

        var server = InstanceUrl(request);
        var connect = MyLifeUrl(options) is { } mylife
            ? $"{mylife}mydocs#server={Uri.EscapeDataString(server)}&token={Uri.EscapeDataString(token)}"
            : null;
        return TypedResults.Ok(new MyLifeConnectResult(token, server, connect));
    }

    private static async Task<IResult> DisconnectAsync(ClaimsPrincipal principal, ObjectAccess access, MyDocsDbContext db, AuditLog audit, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (await db.MyLifeLinks.FirstOrDefaultAsync(l => l.UserId == caller.UserId, ct) is { } link)
        {
            db.MyLifeLinks.Remove(link);
            await db.Tasks.Where(t => t.RuleKey == $"mylife:{caller.PersonId}" && t.State != TaskStates.Done && t.State != TaskStates.Discarded)
                .ExecuteUpdateAsync(t => t.SetProperty(x => x.State, TaskStates.Discarded).SetProperty(x => x.ClosedAt, access.Now).SetProperty(x => x.ClosedBy, caller.UserId), ct);
            audit.Add(AuditActions.MyLifeRevoked, caller.UserId, caller.PersonId);
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> ObjectFlagAsync(Guid id, MyLifeFlagRequest request, ClaimsPrincipal principal, ObjectAccess access, MyDocsDbContext db, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (await access.Visible(caller).FirstOrDefaultAsync(o => o.Id == id, ct) is not { } item)
        {
            return ApiProblems.Of(ObjectErrors.NotFound, StatusCodes.Status404NotFound);
        }

        if (!await access.CanEditAsync(caller, item, ct))
        {
            return ApiProblems.Of(ObjectErrors.Forbidden, StatusCodes.Status403Forbidden);
        }

        if (item.MyLifeSync != request.Enabled)
        {
            db.ObjectHistory.Add(new ObjectHistoryEntry
            {
                ObjectId = item.Id, At = access.Now, ActorId = caller.UserId, Field = "mylife",
                OldValue = item.MyLifeSync ? "true" : "false", NewValue = request.Enabled ? "true" : "false",
            });
            item.MyLifeSync = request.Enabled;
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> DocumentFlagAsync(Guid id, MyLifeFlagRequest request, ClaimsPrincipal principal, ObjectAccess access, MyDocsDbContext db, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (await DocumentEndpoints.FindVisibleAsync(id, caller, access, db, ct) is not { IsDeleted: false, InInbox: false } document)
        {
            return ApiProblems.Of(DocumentErrors.NotFound, StatusCodes.Status404NotFound);
        }

        if (!await DocumentEndpoints.CanEditAsync(id, caller, access, db, ct))
        {
            return ApiProblems.Of(DocumentErrors.Forbidden, StatusCodes.Status403Forbidden);
        }

        var size = await db.DocumentVersions.Where(v => v.DocumentId == id).OrderByDescending(v => v.Number).Select(v => v.Size).FirstAsync(ct);
        if (request.Enabled && size > MyLifeRules.MaxDocumentBytes)
        {
            return ApiProblems.Of(MyLifeErrors.TooLarge);
        }

        document.ToMyLife = request.Enabled;
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ExportAsync(HttpRequest request, MyLifeBridge bridge, CancellationToken ct) =>
        await bridge.AuthenticateAsync(request, ct) is { } auth
            ? TypedResults.Ok(await bridge.BuildAsync(auth.Person, InstanceUrl(request), ct))
            : Unauthorized();

    private static async Task<IResult> ContentAsync(Guid id, HttpRequest request, MyLifeBridge bridge, BlobStore blobs, AuditLog audit, CancellationToken ct)
    {
        if (await bridge.AuthenticateAsync(request, ct) is not { } auth)
        {
            return Unauthorized();
        }

        if (await bridge.DocumentAsync(auth.Person, id, ct) is not { } version)
        {
            return ApiProblems.Of(DocumentErrors.NotFound, StatusCodes.Status404NotFound);
        }

        await audit.WriteAsync(AuditActions.DocumentDownloaded, auth.Link.UserId, auth.Person.Id, documentId: id, details: "myLife", ct: ct);
        var response = request.HttpContext.Response;
        response.Headers.CacheControl = "private, no-store";
        response.ContentLength = version.Size;
        // Nur Bytes für den Tresor: myLife zeigt sie nie unter dieser Adresse an.
        return TypedResults.Stream(output => blobs.CopyToAsync(version.BlobHash, output, request.HttpContext.RequestAborted), "application/octet-stream");
    }

    /// <summary>Quittung (165): myLife hat diesen Stand übernommen. Passt er zum aktuellen, ist der Vorgang erledigt.</summary>
    private static async Task<IResult> SyncedAsync(BridgeSyncedRequest body, HttpRequest request, MyLifeBridge bridge, MyDocsDbContext db, AuditLog audit,
        TimeProvider time, CancellationToken ct)
    {
        if (await bridge.AuthenticateAsync(request, ct) is not { } auth)
        {
            return Unauthorized();
        }

        if (body?.Fingerprint is not { } fingerprint || !FingerprintPattern().IsMatch(fingerprint))
        {
            return ApiProblems.Of(MyLifeErrors.InvalidFingerprint);
        }

        var now = time.GetUtcNow();
        var link = auth.Link;
        link.LastSyncedAt = now;
        link.SyncedFingerprint = fingerprint;
        var current = (await bridge.BuildAsync(auth.Person, string.Empty, ct)).Fingerprint;
        if (current == fingerprint)
        {
            link.PendingSince = null;
            foreach (var task in await db.Tasks.Where(t => t.RuleKey == $"mylife:{auth.Person.Id}" && t.State != TaskStates.Done && t.State != TaskStates.Discarded).ToListAsync(ct))
            {
                task.State = TaskStates.Done;
                task.ClosedAt = now;
                task.ClosedBy = link.UserId;
            }
        }
        else
        {
            // Inzwischen geändert: Der neue Stand wartet auf den nächsten Abgleich.
            link.PendingSince = now;
        }

        audit.Add(AuditActions.MyLifeSynced, link.UserId, auth.Person.Id);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>Adresse dieser Instanz, wie der Browser sie sieht (hinter dem Proxy über die X-Forwarded-Header).</summary>
    internal static string InstanceUrl(HttpRequest request) => $"{request.Scheme}://{request.Host}{request.PathBase}/";

    private static string? MyLifeUrl(IOptions<MyDocsOptions> options) => MyDocsOptions.Origin(options.Value.MyLifeUrl) is { } origin ? origin + "/" : null;

    private static IResult Unauthorized() => ApiProblems.Of(MyLifeErrors.InvalidToken, StatusCodes.Status401Unauthorized);

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex FingerprintPattern();
}
