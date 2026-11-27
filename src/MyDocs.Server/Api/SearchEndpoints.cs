using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Objects;
using MyDocs.Server.Infrastructure.Search;
using MyDocs.Shared.Search;

namespace MyDocs.Server.Api;

/// <summary>
/// Suche über Objekte und Dokumente (25, 26): Volltext mit deutscher Wortstammbildung, dazu Teilwörter und
/// Tippfehler über Trigramme. Die Rechte stecken in der Abfrage; fremde private Treffer gibt es nicht, auch nicht als Anzahl.
/// </summary>
internal static class SearchEndpoints
{
    private const int Limit = 50;
    private const double Similarity = 0.5;

    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/search", SearchAsync).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> SearchAsync(string? q, ClaimsPrincipal principal, ObjectAccess access, MyDocsDbContext db, CancellationToken ct)
    {
        var query = (q ?? string.Empty).Trim();
        if (query.Length < 2)
        {
            return TypedResults.Ok(new List<SearchHitDto>());
        }

        query = query[..Math.Min(query.Length, 200)];
        var pattern = "%" + query.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
        var caller = await access.CallerAsync(principal, ct);

        var objects = await access.Visible(caller)
            .Where(o => o.SearchText != null && (EF.Property<NpgsqlTypes.NpgsqlTsVector>(o, "SearchVector").Matches(EF.Functions.WebSearchToTsQuery("german", query))
                || EF.Functions.ILike(o.SearchText, pattern)
                || SearchFunctions.WordSimilarity(query, o.SearchText) > Similarity))
            .Select(o => new
            {
                o.Id,
                o.Title,
                o.TypeKey,
                o.SearchText,
                Rank = EF.Property<NpgsqlTypes.NpgsqlTsVector>(o, "SearchVector").Rank(EF.Functions.WebSearchToTsQuery("german", query)) + SearchFunctions.WordSimilarity(query, o.SearchText!),
            })
            .OrderByDescending(o => o.Rank).ThenBy(o => o.Title).Take(Limit).ToListAsync(ct);

        var visibleObjects = access.Visible(caller).Select(o => o.Id);
        var documents = await db.Documents
            .Where(d => !d.IsDeleted && d.SearchText != null && db.DocumentObjects.Any(l => l.DocumentId == d.Id && visibleObjects.Contains(l.ObjectId))
                && (EF.Property<NpgsqlTypes.NpgsqlTsVector>(d, "SearchVector").Matches(EF.Functions.WebSearchToTsQuery("german", query))
                    || EF.Functions.ILike(d.SearchText, pattern)
                    || SearchFunctions.WordSimilarity(query, d.SearchText) > Similarity))
            .Select(d => new
            {
                d.Id,
                d.Title,
                d.SearchText,
                Object = (from l in db.DocumentObjects
                          join o in db.Objects on l.ObjectId equals o.Id
                          where l.DocumentId == d.Id && visibleObjects.Contains(o.Id)
                          orderby o.Title
                          select new { o.Id, o.Title }).First(),
                Rank = EF.Property<NpgsqlTypes.NpgsqlTsVector>(d, "SearchVector").Rank(EF.Functions.WebSearchToTsQuery("german", query)) + SearchFunctions.WordSimilarity(query, d.SearchText!),
            })
            .OrderByDescending(d => d.Rank).ThenBy(d => d.Title).Take(Limit).ToListAsync(ct);

        if (caller.AdminView)
        {
            var touched = objects.Select(o => o.Id).Concat(documents.Select(d => d.Object.Id)).Distinct().ToList();
            await access.RecordForeignAccessAsync(caller, await db.Objects.Where(o => touched.Contains(o.Id)).ToListAsync(ct), "search", ct);
        }

        var hits = objects.Select(o => (o.Rank, Hit: new SearchHitDto(SearchKinds.Entry, o.Id, o.Title, o.TypeKey, null, null, Snippet(o.SearchText, query))))
            .Concat(documents.Select(d => (d.Rank, Hit: new SearchHitDto(SearchKinds.Document, d.Id, d.Title, null, d.Object.Id, d.Object.Title, Snippet(d.SearchText, query)))))
            .OrderByDescending(h => h.Rank).Take(Limit).Select(h => h.Hit).ToList();
        return TypedResults.Ok(hits);
    }

    /// <summary>Die Zeile des Suchtexts, in der der Begriff vorkommt (ohne Titel), sonst nichts.</summary>
    private static string? Snippet(string? text, string query)
    {
        var lines = (text ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var line = lines.Skip(1).FirstOrDefault(l => words.Any(w => l.Contains(w, StringComparison.CurrentCultureIgnoreCase)));
        return line is null ? null : line.Length > 160 ? line[..160] + "…" : line;
    }
}
