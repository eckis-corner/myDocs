using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure;
using MyDocs.Server.Infrastructure.Audit;
using MyDocs.Server.Infrastructure.Files;
using MyDocs.Server.Infrastructure.Objects;
using MyDocs.Server.Infrastructure.Processing;
using MyDocs.Server.Infrastructure.Search;
using MyDocs.Shared.Audit;
using MyDocs.Shared.Documents;
using MyDocs.Shared.Household;

namespace MyDocs.Server.Api;

/// <summary>
/// Check-out und Check-in (17, 156): Wer ausgecheckt hat, lädt jede gespeicherte Fassung als neue Version hoch;
/// andere sehen „in Bearbeitung von …“ und laden nichts hoch. Die Sperre hält 24 Stunden ab dem letzten Lebenszeichen.
/// Hat sich das Dokument trotzdem geändert (Sperre abgelaufen oder aufgehoben), entsteht keine stille Überschreibung,
/// sondern ein eigenes Konfliktdokument an denselben Einträgen.
/// </summary>
internal static class CheckoutEndpoints
{
    public static IEndpointRouteBuilder MapCheckoutEndpoints(this IEndpointRouteBuilder app, long maxRequestBytes)
    {
        var group = app.MapGroup("/api/documents/{id:guid}/checkout").RequireAuthorization(PersonEndpoints.MemberPolicy);
        group.MapPost("", CheckOutAsync);
        group.MapPut("", RenewAsync);
        group.MapDelete("", ReleaseAsync);
        group.MapPost("/versions", UploadAsync).DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(maxRequestBytes));
        return app;
    }

    private static async Task<IResult> CheckOutAsync(Guid id, ClaimsPrincipal principal, ObjectAccess access, MyDocsDbContext db, AuditLog audit, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (await EditableAsync(id, caller, access, db, ct) is not { } document)
        {
            return ApiProblems.Of(DocumentErrors.NotFound, StatusCodes.Status404NotFound);
        }

        var now = access.Now;
        if (DocumentEndpoints.IsLocked(document, now) && document.LockedBy != caller.UserId)
        {
            return ApiProblems.Of(DocumentErrors.Locked, StatusCodes.Status409Conflict);
        }

        document.LockedBy = caller.UserId;
        document.LockedAt = DocumentEndpoints.IsLocked(document, now) ? document.LockedAt : now;
        document.LockExpiresAt = now + CheckoutRules.LockLifetime;
        var number = await db.DocumentVersions.Where(v => v.DocumentId == id).MaxAsync(v => v.Number, ct);
        audit.Add(AuditActions.DocumentCheckedOut, caller.UserId, await DocumentEndpoints.OwnerAsync(db, id, ct), documentId: id, title: document.Title);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new CheckoutDto(await DocumentEndpoints.ToDtoAsync(document, caller, access, db, ct), number, document.LockExpiresAt.Value));
    }

    /// <summary>Lebenszeichen des Clients: Die Sperre hält wieder 24 Stunden.</summary>
    private static async Task<IResult> RenewAsync(Guid id, ClaimsPrincipal principal, ObjectAccess access, MyDocsDbContext db, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (await EditableAsync(id, caller, access, db, ct) is not { } document)
        {
            return ApiProblems.Of(DocumentErrors.NotFound, StatusCodes.Status404NotFound);
        }

        if (document.LockedBy != caller.UserId || !DocumentEndpoints.IsLocked(document, access.Now))
        {
            return ApiProblems.Of(DocumentErrors.NotLocked, StatusCodes.Status409Conflict);
        }

        document.LockExpiresAt = access.Now + CheckoutRules.LockLifetime;
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>Einchecken bzw. Sperre aufheben: durch die Person mit der Sperre oder einen Administrator.</summary>
    private static async Task<IResult> ReleaseAsync(Guid id, ClaimsPrincipal principal, ObjectAccess access, MyDocsDbContext db, AuditLog audit, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (await DocumentEndpoints.FindVisibleAsync(id, caller, access, db, ct) is not { } document)
        {
            return ApiProblems.Of(DocumentErrors.NotFound, StatusCodes.Status404NotFound);
        }

        if (document.LockedBy is null)
        {
            return TypedResults.NoContent();
        }

        if (document.LockedBy != caller.UserId && !principal.IsInRole(HouseholdRoles.Admin))
        {
            return ApiProblems.Of(DocumentErrors.Forbidden, StatusCodes.Status403Forbidden);
        }

        var details = document.LockedBy == caller.UserId ? null : "Sperre aufgehoben";
        document.LockedBy = null;
        document.LockedAt = null;
        document.LockExpiresAt = null;
        audit.Add(AuditActions.DocumentCheckedIn, caller.UserId, await DocumentEndpoints.OwnerAsync(db, id, ct), documentId: id, title: document.Title, details: details);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> UploadAsync(Guid id, int baseVersion, HttpRequest request, ClaimsPrincipal principal, ObjectAccess access, MyDocsDbContext db,
        BlobStore blobs, ProcessingScheduler processing, AuditLog audit, IOptions<MyDocsOptions> options, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (await EditableAsync(id, caller, access, db, ct) is not { } document)
        {
            return ApiProblems.Of(DocumentErrors.NotFound, StatusCodes.Status404NotFound);
        }

        var upload = await DocumentEndpoints.ReadFileAsync(request, blobs, options.Value, ct);
        if (upload.Problem is { } problem)
        {
            return problem;
        }

        var now = access.Now;
        var latest = await db.DocumentVersions.Where(v => v.DocumentId == id).MaxAsync(v => v.Number, ct);
        var holder = document.LockedBy == caller.UserId && DocumentEndpoints.IsLocked(document, now);
        var lockedByOther = DocumentEndpoints.IsLocked(document, now) && document.LockedBy != caller.UserId;
        if (holder && latest == baseVersion)
        {
            db.DocumentVersions.Add(DocumentEndpoints.Version(id, latest + 1, upload, caller.UserId, now));
            await processing.EnqueueAsync(upload.Hash, upload.FileName, ct);
            document.ContentText = await processing.TextAsync(upload.Hash, ct);
            document.UpdatedAt = now;
            document.LockExpiresAt = now + CheckoutRules.LockLifetime;
            await SearchIndex.UpdateAsync(db, document, ct);
            audit.Add(AuditActions.DocumentVersionAdded, caller.UserId, await DocumentEndpoints.OwnerAsync(db, id, ct), documentId: id, title: document.Title,
                details: $"{latest + 1}: {upload.FileName} (Check-out)");
            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(new CheckoutUploadResult(await DocumentEndpoints.ToDtoAsync(document, caller, access, db, ct), false, latest + 1));
        }

        // Konflikt: Jemand anderes hat inzwischen eine Version hochgeladen oder hält die Sperre. Nichts wird überschrieben.
        var name = await db.Users.Where(u => u.Id == caller.UserId).Select(u => u.DisplayName).FirstAsync(ct);
        var title = $"{document.Title} (Konflikt {name}, {TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)})";
        var conflict = new Document
        {
            Id = Guid.CreateVersion7(),
            Title = title.Length > 200 ? title[..200] : title,
            Kind = document.Kind,
            DocumentDate = document.DocumentDate,
            Note = $"Konfliktversion zu „{document.Title}“: beim Speichern gab es dort schon Version {latest}{(lockedByOther ? ", in Bearbeitung von jemand anderem" : string.Empty)}.",
            CreatedAt = now,
            CreatedBy = caller.UserId,
            UpdatedAt = now,
            LockedBy = caller.UserId,
            LockedAt = now,
            LockExpiresAt = now + CheckoutRules.LockLifetime,
        };
        db.Documents.Add(conflict);
        var links = await db.DocumentObjects.Where(l => l.DocumentId == id).Select(l => l.ObjectId).ToListAsync(ct);
        db.DocumentObjects.AddRange(links.Select(o => new DocumentObject { DocumentId = conflict.Id, ObjectId = o }));
        db.DocumentVersions.Add(DocumentEndpoints.Version(conflict.Id, 1, upload, caller.UserId, now));
        await processing.EnqueueAsync(upload.Hash, upload.FileName, ct);
        conflict.ContentText = await processing.TextAsync(upload.Hash, ct);
        await SearchIndex.UpdateAsync(db, conflict, ct);
        if (holder)
        {
            // Die eigene Sperre am Original ist damit erledigt.
            document.LockedBy = null;
            document.LockedAt = null;
            document.LockExpiresAt = null;
        }

        audit.Add(AuditActions.DocumentUploaded, caller.UserId, await DocumentEndpoints.OwnerAsync(db, id, ct), documentId: conflict.Id, title: conflict.Title, details: $"Konflikt: {upload.FileName}");
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(new CheckoutUploadResult(await DocumentEndpoints.ToDtoAsync(conflict, caller, access, db, ct), true, 1));
    }

    /// <summary>Sichtbar, nicht gelöscht, zugeordnet und bearbeitbar.</summary>
    private static async Task<Document?> EditableAsync(Guid id, Caller caller, ObjectAccess access, MyDocsDbContext db, CancellationToken ct) =>
        await DocumentEndpoints.FindVisibleAsync(id, caller, access, db, ct) is { IsDeleted: false, InInbox: false } document
            && await DocumentEndpoints.CanEditAsync(id, caller, access, db, ct)
            ? document
            : null;
}
