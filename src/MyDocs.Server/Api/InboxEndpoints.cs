using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure;
using MyDocs.Server.Infrastructure.Audit;
using MyDocs.Server.Infrastructure.Files;
using MyDocs.Server.Infrastructure.Inbox;
using MyDocs.Server.Infrastructure.Objects;
using MyDocs.Server.Infrastructure.Processing;
using MyDocs.Server.Infrastructure.Search;
using MyDocs.Shared.Audit;
using MyDocs.Shared.Documents;
using MyDocs.Shared.Household;
using MyDocs.Shared.Tasks;

namespace MyDocs.Server.Api;

/// <summary>
/// Posteingang (20, 142–145): Was über Kamera, Scanner, Mail oder ohne Eintrag hereinkommt, wartet hier, bis es
/// zugeordnet ist. Zuordnen setzt Titel, Dokumentart und Datum, hängt das Dokument an einen oder mehrere Einträge und
/// legt auf Wunsch einen Vorgang an.
/// </summary>
internal static class InboxEndpoints
{
    public static IEndpointRouteBuilder MapInboxEndpoints(this IEndpointRouteBuilder app, long maxRequestBytes)
    {
        var group = app.MapGroup("/api/inbox").RequireAuthorization(PersonEndpoints.MemberPolicy);
        group.MapGet("", ListAsync);
        group.MapPost("", UploadAsync).DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(maxRequestBytes));
        group.MapPost("/{id:guid}/assign", AssignAsync);
        group.MapPut("/{id:guid}/owner", OwnerAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(bool? discarded, ClaimsPrincipal principal, ObjectAccess access, MyDocsDbContext db, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        var deleted = discarded == true;
        var documents = await db.Documents.Where(InboxAccess.VisibleTo(caller)).Where(d => d.IsDeleted == deleted)
            .OrderBy(d => d.CreatedAt).ToListAsync(ct);
        var result = new List<InboxItemDto>();
        foreach (var document in documents)
        {
            result.Add(await ToItemAsync(document, caller, access, db, ct));
        }

        return TypedResults.Ok(result);
    }

    /// <summary>Hochladen ohne Eintrag: in den eigenen Posteingang oder in den des Haushalts.</summary>
    private static async Task<IResult> UploadAsync(bool? household, HttpRequest request, ClaimsPrincipal principal, ObjectAccess access,
        MyDocsDbContext db, BlobStore blobs, ProcessingScheduler processing, AuditLog audit, IOptions<MyDocsOptions> options, TimeProvider time, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (caller.PersonId is null && household != true)
        {
            return ApiProblems.Of(DocumentErrors.Forbidden, StatusCodes.Status403Forbidden);
        }

        var upload = await DocumentEndpoints.ReadFileAsync(request, blobs, options.Value, ct);
        if (upload.Problem is { } problem)
        {
            return problem;
        }

        var document = await InboxIntake.AddAsync(db, processing, audit, upload.Hash, upload.Size, upload.FileName, upload.ContentType,
            household == true ? null : caller.PersonId, DocumentSources.Upload, null, caller.UserId, time.GetUtcNow(), ct);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ToItemAsync(document, caller, access, db, ct));
    }

    private static async Task<IResult> AssignAsync(Guid id, InboxAssignRequest request, ClaimsPrincipal principal, ObjectAccess access,
        MyDocsDbContext db, AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (await db.Documents.Where(InboxAccess.VisibleTo(caller)).FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted, ct) is not { } document)
        {
            return ApiProblems.Of(DocumentErrors.NotFound, StatusCodes.Status404NotFound);
        }

        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 200)
        {
            return ApiProblems.Of(DocumentErrors.TitleRequired);
        }

        if (!DocumentKinds.IsValid(request.Kind))
        {
            return ApiProblems.Of(DocumentErrors.InvalidKind);
        }

        var wanted = request.ObjectIds.Distinct().ToList();
        if (wanted.Count == 0)
        {
            return ApiProblems.Of(DocumentErrors.NeedsObject);
        }

        var objects = await access.Visible(caller).Where(o => wanted.Contains(o.Id)).ToListAsync(ct);
        if (objects.Count != wanted.Count)
        {
            return ApiProblems.Of(DocumentErrors.Forbidden, StatusCodes.Status403Forbidden);
        }

        foreach (var item in objects)
        {
            if (!await access.CanEditAsync(caller, item, ct))
            {
                return ApiProblems.Of(DocumentErrors.Forbidden, StatusCodes.Status403Forbidden);
            }
        }

        var wantsTask = request.TaskTitle is not null || request.TaskDueOn is not null;
        if (wantsTask && (string.IsNullOrWhiteSpace(request.TaskTitle) || request.TaskTitle.Trim().Length > 200))
        {
            return ApiProblems.Of(DocumentErrors.TaskTitleRequired);
        }

        var now = time.GetUtcNow();
        document.Title = request.Title.Trim();
        document.Kind = request.Kind;
        document.DocumentDate = request.DocumentDate;
        document.InInbox = false;
        document.InboxPersonId = null;
        document.UpdatedAt = now;
        db.DocumentObjects.AddRange(objects.Select(o => new DocumentObject { DocumentId = id, ObjectId = o.Id }));
        await SearchIndex.UpdateAsync(db, document, ct);

        if (wantsTask)
        {
            var task = new WorkTask
            {
                Id = Guid.CreateVersion7(),
                Kind = TaskKinds.Task,
                Title = request.TaskTitle!.Trim(),
                ResponsiblePersonId = caller.PersonId,
                DueOn = request.TaskDueOn,
                CreatedAt = now,
                CreatedBy = caller.UserId,
            };
            db.Tasks.Add(task);
            db.TaskObjects.AddRange(objects.Select(o => new TaskObject { TaskId = task.Id, ObjectId = o.Id }));
        }

        foreach (var item in objects)
        {
            audit.Add(AuditActions.DocumentAssigned, caller.UserId, item.OwnerPersonId, item.Id, id, document.Title, item.Title);
        }

        await db.SaveChangesAsync(ct);
        // Ist der Posteingang jetzt leer, schließt die Wiedervorlage den Vorgang „Posteingang abarbeiten“ beim nächsten Lauf.
        return TypedResults.Ok(await DocumentEndpoints.ToDtoAsync(document, caller, access, db, ct));
    }

    /// <summary>Privat → Haushalt durch die Person; Haushalt → privat übernimmt jedes Mitglied für sich.</summary>
    private static async Task<IResult> OwnerAsync(Guid id, InboxOwnerRequest request, ClaimsPrincipal principal, ObjectAccess access,
        MyDocsDbContext db, TimeProvider time, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (await db.Documents.Where(InboxAccess.VisibleTo(caller)).FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted, ct) is not { } document)
        {
            return ApiProblems.Of(DocumentErrors.NotFound, StatusCodes.Status404NotFound);
        }

        if (!request.Household && caller.PersonId is null)
        {
            return ApiProblems.Of(DocumentErrors.Forbidden, StatusCodes.Status403Forbidden);
        }

        document.InboxPersonId = request.Household ? null : caller.PersonId;
        document.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ToItemAsync(document, caller, access, db, ct));
    }

    internal static async Task<InboxItemDto> ToItemAsync(Document document, Caller caller, ObjectAccess access, MyDocsDbContext db, CancellationToken ct)
    {
        var dto = await DocumentEndpoints.ToDtoAsync(document, caller, access, db, ct);
        return new InboxItemDto(document.Id, document.Title, document.Kind, document.DocumentDate, document.Source, document.SourceDetail,
            document.InboxPersonId is null, document.CreatedAt, dto.Current, document.DeletedAt);
    }
}
