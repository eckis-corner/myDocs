using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Documents;
using MyDocs.Server.Infrastructure.Objects;
using MyDocs.Shared.Documents;

namespace MyDocs.Server.Api;

/// <summary>
/// Aufräumen (28, 174): Dokumente, deren Aufbewahrung abgelaufen ist. myDocs löscht nie selbst; die Person legt sie in
/// den Papierkorb oder behält sie länger.
/// </summary>
internal static class CleanupEndpoints
{
    public static IEndpointRouteBuilder MapCleanupEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/cleanup", ListAsync).RequireAuthorization();
        app.MapPut("/api/documents/{id:guid}/retention", RetentionAsync).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> ListAsync(ClaimsPrincipal principal, ObjectAccess access, MyDocsDbContext db, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        var visible = access.Visible(caller).Select(o => o.Id);
        var links = await db.DocumentObjects.Where(l => visible.Contains(l.ObjectId)).ToListAsync(ct);
        var documentIds = links.Select(l => l.DocumentId).Distinct().ToList();
        var documents = await db.Documents.Where(d => documentIds.Contains(d.Id) && !d.IsDeleted && !d.InInbox).ToListAsync(ct);
        var objects = await LinkedAsync(db, documentIds, ct);
        var today = access.Today;
        var result = new List<CleanupItemDto>();
        foreach (var document in documents)
        {
            var linked = objects.GetValueOrDefault(document.Id) ?? [];
            if (Retention.Until(document, linked) is not { } until || !Retention.IsDue(until, today))
            {
                continue;
            }

            var rule = RetentionRules.For(document.Kind);
            result.Add(new CleanupItemDto(document.Id, document.Title, document.Kind, document.DocumentDate, until, document.RetainUntil is null ? rule?.Basis : null,
                rule?.Years ?? 0, [.. links.Where(l => l.DocumentId == document.Id).Select(l => l.ObjectId)],
                await DocumentEndpoints.CanEditAsync(document.Id, caller, access, db, ct)));
        }

        return TypedResults.Ok(result.OrderBy(r => r.RetainUntil).ThenBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase).ToList());
    }

    private static async Task<IResult> RetentionAsync(Guid id, RetentionRequest request, ClaimsPrincipal principal, ObjectAccess access, MyDocsDbContext db, CancellationToken ct)
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

        document.RetainUntil = request.Forever ? RetentionRules.Forever : request.Until;
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>Zugeordnete Einträge je Dokument, auch gelöschte (die zählen bei „Ende des Eintrags“ nicht mit).</summary>
    internal static async Task<Dictionary<Guid, List<DocObject>>> LinkedAsync(MyDocsDbContext db, IReadOnlyCollection<Guid> documentIds, CancellationToken ct) =>
        (await (from l in db.DocumentObjects
                join o in db.Objects on l.ObjectId equals o.Id
                where documentIds.Contains(l.DocumentId)
                select new { l.DocumentId, Object = o }).ToListAsync(ct))
        .GroupBy(x => x.DocumentId).ToDictionary(g => g.Key, g => g.Select(x => x.Object).ToList());
}
