using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MyDocs.Server.Data;
using MyDocs.Shared.Audit;

namespace MyDocs.Server.Api;

/// <summary>
/// Protokoll lesen (35): Jede Person sieht ihre eigenen Aktionen und alles, was andere mit ihren privaten Daten tun,
/// auch jede Admin-Einsicht (31). Der Administrator sieht das ganze Protokoll.
/// </summary>
internal static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/audit", MineAsync).RequireAuthorization();
        app.MapGet("/api/admin/audit", AllAsync).RequireAuthorization(Roles.Admin);
        return app;
    }

    private static async Task<IResult> MineAsync(int? take, ClaimsPrincipal principal, MyDocsDbContext db, CancellationToken ct)
    {
        var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var personId = await db.Persons.Where(p => p.UserId == userId).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
        var query = db.AuditLog.Where(a => a.ActorId == userId || (personId != null && a.OwnerPersonId == personId));
        return TypedResults.Ok(await ReadAsync(query, db, Math.Clamp(take ?? 200, 1, 1000), ct));
    }

    private static async Task<IResult> AllAsync(int? take, string? action, MyDocsDbContext db, CancellationToken ct)
    {
        var query = db.AuditLog.AsQueryable();
        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(a => a.Action == action);
        }

        return TypedResults.Ok(await ReadAsync(query, db, Math.Clamp(take ?? 500, 1, 5000), ct));
    }

    private static Task<List<AuditEntryDto>> ReadAsync(IQueryable<AuditEntry> query, MyDocsDbContext db, int take, CancellationToken ct) =>
        (from a in query
         join u in db.Users on a.ActorId equals u.Id into actors
         from actor in actors.DefaultIfEmpty()
         orderby a.At descending, a.Id descending
         select new AuditEntryDto(a.Id, a.At, actor != null ? actor.DisplayName : null, a.Action, a.ObjectId, a.DocumentId, a.Title, a.Ip, a.Details))
        .Take(take).ToListAsync(ct);
}
