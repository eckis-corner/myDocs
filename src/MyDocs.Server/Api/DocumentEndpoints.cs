using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure;
using MyDocs.Server.Infrastructure.Files;
using MyDocs.Server.Infrastructure.Objects;
using MyDocs.Shared.Documents;

namespace MyDocs.Server.Api;

/// <summary>
/// Dokumente an Objekten (M1.5). Rechte erbt ein Dokument von seinen Objekten: Wer eines sieht, sieht das Dokument;
/// wer eines bearbeiten darf, lädt Versionen hoch und ändert die Angaben (33).
/// </summary>
internal static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app, long maxRequestBytes)
    {
        app.MapGet("/api/objects/{id:guid}/documents", ListAsync).RequireAuthorization();
        app.MapPost("/api/objects/{id:guid}/documents", UploadAsync).RequireAuthorization()
            .DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(maxRequestBytes));

        var group = app.MapGroup("/api/documents").RequireAuthorization();
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapPost("/{id:guid}/versions", AddVersionAsync).DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(maxRequestBytes));
        group.MapGet("/{id:guid}/content", ContentAsync);
        group.MapPut("/{id:guid}/objects", LinkAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
        group.MapPost("/{id:guid}/restore", RestoreAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(Guid id, ClaimsPrincipal principal, ObjectAccess access, MyDocsDbContext db, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (!await VisibleObjects(access, caller).AnyAsync(o => o.Id == id, ct))
        {
            return NotFound();
        }

        var ids = await db.DocumentObjects.Where(l => l.ObjectId == id).Select(l => l.DocumentId).ToListAsync(ct);
        var documents = await db.Documents.Where(d => ids.Contains(d.Id) && !d.IsDeleted).OrderByDescending(d => d.DocumentDate ?? DateOnly.MinValue).ThenBy(d => d.Title).ToListAsync(ct);
        var result = new List<DocumentDto>();
        foreach (var document in documents)
        {
            result.Add(await ToDtoAsync(document, caller, access, db, ct));
        }

        return TypedResults.Ok(result);
    }

    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal principal, ObjectAccess access, MyDocsDbContext db, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        return await FindVisibleAsync(id, caller, access, db, ct) is { } document ? TypedResults.Ok(await ToDtoAsync(document, caller, access, db, ct)) : NotFound();
    }

    private static async Task<IResult> UploadAsync(Guid id, HttpRequest request, ClaimsPrincipal principal, ObjectAccess access, MyDocsDbContext db,
        BlobStore blobs, IOptions<MyDocsOptions> options, TimeProvider time, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (await access.Visible(caller).FirstOrDefaultAsync(o => o.Id == id, ct) is not { } item)
        {
            return NotFound();
        }

        if (!await access.CanEditAsync(caller, item, ct))
        {
            return Forbidden();
        }

        var upload = await ReadFileAsync(request, blobs, options.Value, ct);
        if (upload.Problem is { } problem)
        {
            return problem;
        }

        var now = time.GetUtcNow();
        var document = new Document
        {
            Id = Guid.CreateVersion7(),
            Title = TitleFrom(upload.FileName),
            CreatedAt = now,
            CreatedBy = caller.UserId,
            UpdatedAt = now,
        };
        db.Documents.Add(document);
        db.DocumentObjects.Add(new DocumentObject { DocumentId = document.Id, ObjectId = item.Id });
        db.DocumentVersions.Add(Version(document.Id, 1, upload, caller.UserId, now));
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ToDtoAsync(document, caller, access, db, ct));
    }

    private static async Task<IResult> AddVersionAsync(Guid id, HttpRequest request, ClaimsPrincipal principal, ObjectAccess access, MyDocsDbContext db,
        BlobStore blobs, IOptions<MyDocsOptions> options, TimeProvider time, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (await FindVisibleAsync(id, caller, access, db, ct) is not { IsDeleted: false } document)
        {
            return NotFound();
        }

        if (!await CanEditAsync(document.Id, caller, access, db, ct))
        {
            return Forbidden();
        }

        var upload = await ReadFileAsync(request, blobs, options.Value, ct);
        if (upload.Problem is { } problem)
        {
            return problem;
        }

        var number = await db.DocumentVersions.Where(v => v.DocumentId == id).MaxAsync(v => v.Number, ct) + 1;
        var now = time.GetUtcNow();
        db.DocumentVersions.Add(Version(id, number, upload, caller.UserId, now));
        document.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ToDtoAsync(document, caller, access, db, ct));
    }

    private static async Task<IResult> ContentAsync(Guid id, int? version, bool? download, ClaimsPrincipal principal, ObjectAccess access,
        MyDocsDbContext db, BlobStore blobs, HttpResponse response, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (await FindVisibleAsync(id, caller, access, db, ct) is null)
        {
            return NotFound();
        }

        var query = db.DocumentVersions.Where(v => v.DocumentId == id);
        var file = version is { } number
            ? await query.FirstOrDefaultAsync(v => v.Number == number, ct)
            : await query.OrderByDescending(v => v.Number).FirstOrDefaultAsync(ct);
        if (file is null)
        {
            return NotFound();
        }

        // Nur ausgewählte Typen zeigt der Browser direkt; alles andere ist ein Download ohne Vorschau.
        var inline = download != true && Previews.InlineTypes.Contains(file.ContentType);
        var disposition = new ContentDispositionHeaderValue(inline ? "inline" : "attachment");
        disposition.SetHttpFileName(file.FileName);
        response.Headers.ContentDisposition = disposition.ToString();
        response.Headers.CacheControl = "private, no-store";
        response.ContentLength = file.Size;
        return TypedResults.Stream(output => blobs.CopyToAsync(file.BlobHash, output, ct), inline ? file.ContentType : "application/octet-stream");
    }

    private static async Task<IResult> UpdateAsync(Guid id, DocumentUpdateRequest request, ClaimsPrincipal principal, ObjectAccess access,
        MyDocsDbContext db, TimeProvider time, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (await FindVisibleAsync(id, caller, access, db, ct) is not { IsDeleted: false } document)
        {
            return NotFound();
        }

        if (!await CanEditAsync(id, caller, access, db, ct))
        {
            return Forbidden();
        }

        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200)
        {
            return ApiProblems.Of(DocumentErrors.TitleRequired);
        }

        document.Title = request.Title.Trim();
        document.DocumentDate = request.DocumentDate;
        document.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim()[..Math.Min(request.Note.Trim().Length, 2000)];
        document.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ToDtoAsync(document, caller, access, db, ct));
    }

    /// <summary>
    /// Neue Zuordnung (21). Objekte, die der Aufrufer nicht sieht, bleiben unverändert zugeordnet; hinzufügen und
    /// entfernen darf er nur bei Objekten, die er bearbeiten kann.
    /// </summary>
    private static async Task<IResult> LinkAsync(Guid id, DocumentObjectsRequest request, ClaimsPrincipal principal, ObjectAccess access,
        MyDocsDbContext db, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (await FindVisibleAsync(id, caller, access, db, ct) is not { IsDeleted: false } document)
        {
            return NotFound();
        }

        var current = await db.DocumentObjects.Where(l => l.DocumentId == id).ToListAsync(ct);
        var wanted = request.ObjectIds.Distinct().ToHashSet();
        var changed = current.Select(l => l.ObjectId).ToHashSet();
        changed.SymmetricExceptWith(wanted);
        foreach (var objectId in changed)
        {
            if (await access.Visible(caller).FirstOrDefaultAsync(o => o.Id == objectId, ct) is not { } item || !await access.CanEditAsync(caller, item, ct))
            {
                return Forbidden();
            }
        }

        var remaining = current.Count(l => wanted.Contains(l.ObjectId)) + wanted.Count(w => current.All(l => l.ObjectId != w));
        if (remaining == 0)
        {
            return ApiProblems.Of(DocumentErrors.NeedsObject);
        }

        db.DocumentObjects.RemoveRange(current.Where(l => !wanted.Contains(l.ObjectId)));
        db.DocumentObjects.AddRange(wanted.Where(w => current.All(l => l.ObjectId != w)).Select(w => new DocumentObject { DocumentId = id, ObjectId = w }));
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ToDtoAsync(document, caller, access, db, ct));
    }

    private static async Task<IResult> DeleteAsync(Guid id, ClaimsPrincipal principal, ObjectAccess access, MyDocsDbContext db, TimeProvider time, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (await FindVisibleAsync(id, caller, access, db, ct) is not { IsDeleted: false } document)
        {
            return NotFound();
        }

        if (!await CanEditAsync(id, caller, access, db, ct))
        {
            return Forbidden();
        }

        document.IsDeleted = true;
        document.DeletedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> RestoreAsync(Guid id, ClaimsPrincipal principal, ObjectAccess access, MyDocsDbContext db, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (await FindVisibleAsync(id, caller, access, db, ct) is not { IsDeleted: true } document)
        {
            return NotFound();
        }

        if (!await CanEditAsync(id, caller, access, db, ct))
        {
            return Forbidden();
        }

        document.IsDeleted = false;
        document.DeletedAt = null;
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ToDtoAsync(document, caller, access, db, ct));
    }

    private sealed record Upload(string FileName, string ContentType, string Hash, long Size, IResult? Problem);

    /// <summary>Liest die erste Datei aus dem Multipart-Body und verschlüsselt sie beim Lesen (kein Klartext auf der Platte).</summary>
    private static async Task<Upload> ReadFileAsync(HttpRequest request, BlobStore blobs, MyDocsOptions options, CancellationToken ct)
    {
        var none = new Upload(string.Empty, string.Empty, string.Empty, 0, ApiProblems.Of(DocumentErrors.NoFile));
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var type) || HeaderUtilities.RemoveQuotes(type.Boundary).Value is not { Length: > 0 } boundary)
        {
            return none;
        }

        var reader = new MultipartReader(boundary, request.Body);
        while (await reader.ReadNextSectionAsync(ct) is { } section)
        {
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition) || !disposition.IsFileDisposition())
            {
                continue;
            }

            var fileName = CleanFileName(HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value);
            var contentType = string.IsNullOrWhiteSpace(section.ContentType) ? "application/octet-stream" : section.ContentType.Split(';')[0].Trim().ToLowerInvariant();
            try
            {
                var (hash, size) = await blobs.StoreAsync(section.Body, options.MaxFileSizeMegabytes * 1024L * 1024L, ct);
                return new Upload(fileName, contentType, hash, size, null);
            }
            catch (FileTooLargeException)
            {
                return none with { Problem = ApiProblems.Of(DocumentErrors.TooLarge, StatusCodes.Status413PayloadTooLarge) };
            }
        }

        return none;
    }

    private static string CleanFileName(string? name)
    {
        var file = Path.GetFileName(name ?? string.Empty).Trim();
        file = new string([.. file.Where(c => !char.IsControl(c) && c is not ('"' or '\\' or '/'))]);
        return string.IsNullOrWhiteSpace(file) ? "Dokument" : file[..Math.Min(file.Length, 255)];
    }

    private static string TitleFrom(string fileName)
    {
        var title = Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ').Trim();
        return string.IsNullOrWhiteSpace(title) ? fileName : title[..Math.Min(title.Length, 200)];
    }

    private static DocumentVersion Version(Guid documentId, int number, Upload upload, Guid user, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(),
        DocumentId = documentId,
        Number = number,
        BlobHash = upload.Hash,
        FileName = upload.FileName,
        ContentType = upload.ContentType,
        Size = upload.Size,
        CreatedAt = at,
        CreatedBy = user,
    };

    /// <summary>Sichtbare Objekte einschließlich Papierkorb: Dokumente bleiben über ein gelöschtes Objekt erreichbar.</summary>
    private static IQueryable<DocObject> VisibleObjects(ObjectAccess access, Caller caller) =>
        access.Visible(caller).Concat(access.Visible(caller, deleted: true));

    private static async Task<Document?> FindVisibleAsync(Guid id, Caller caller, ObjectAccess access, MyDocsDbContext db, CancellationToken ct)
    {
        var visible = VisibleObjects(access, caller).Select(o => o.Id);
        return await db.Documents.FirstOrDefaultAsync(d => d.Id == id && db.DocumentObjects.Any(l => l.DocumentId == d.Id && visible.Contains(l.ObjectId)), ct);
    }

    private static async Task<bool> CanEditAsync(Guid documentId, Caller caller, ObjectAccess access, MyDocsDbContext db, CancellationToken ct)
    {
        var objectIds = db.DocumentObjects.Where(l => l.DocumentId == documentId).Select(l => l.ObjectId);
        foreach (var item in await access.Visible(caller).Where(o => objectIds.Contains(o.Id)).ToListAsync(ct))
        {
            if (await access.CanEditAsync(caller, item, ct))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<DocumentDto> ToDtoAsync(Document document, Caller caller, ObjectAccess access, MyDocsDbContext db, CancellationToken ct)
    {
        var versions = await (from v in db.DocumentVersions
                              join u in db.Users on v.CreatedBy equals u.Id into users
                              from user in users.DefaultIfEmpty()
                              where v.DocumentId == document.Id
                              orderby v.Number descending
                              select new DocumentVersionDto(v.Number, v.FileName, v.ContentType, v.Size, v.CreatedAt, user != null ? user.DisplayName : "?")).ToListAsync(ct);
        var visible = VisibleObjects(access, caller).Select(o => o.Id);
        var objects = await db.DocumentObjects.Where(l => l.DocumentId == document.Id && visible.Contains(l.ObjectId)).Select(l => l.ObjectId).ToListAsync(ct);
        return new DocumentDto(document.Id, document.Title, document.DocumentDate, document.Note, versions, objects,
            !document.IsDeleted && await CanEditAsync(document.Id, caller, access, db, ct), document.UpdatedAt, document.DeletedAt);
    }

    private static IResult NotFound() => ApiProblems.Of(DocumentErrors.NotFound, StatusCodes.Status404NotFound);

    private static IResult Forbidden() => ApiProblems.Of(DocumentErrors.Forbidden, StatusCodes.Status403Forbidden);
}
