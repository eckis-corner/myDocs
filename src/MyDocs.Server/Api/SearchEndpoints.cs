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
        // Dokumente: Angaben wie bei Einträgen, dazu der erkannte Text der aktuellen Version (M3.2). Für den langen Text
        // gelten Wortstämme und Teilwörter, aber keine Tippfehler-Ähnlichkeit (sie müsste jeden Text ganz durchgehen).
        // Auch der eigene Posteingang und der des Haushalts (142).
        var person = caller.PersonId;
        var member = caller.IsMember;
        var documents = await db.Documents
            .Where(d => !d.IsDeleted && (d.SearchText != null || d.ContentText != null)
                && (db.DocumentObjects.Any(l => l.DocumentId == d.Id && visibleObjects.Contains(l.ObjectId))
                    || (d.InInbox && member && (d.InboxPersonId == null || (person != null && d.InboxPersonId == person))))
                && (EF.Property<NpgsqlTypes.NpgsqlTsVector>(d, "SearchVector").Matches(EF.Functions.WebSearchToTsQuery("german", query))
                    || EF.Functions.ILike(d.SearchText!, pattern)
                    || SearchFunctions.WordSimilarity(query, d.SearchText!) > Similarity
                    || EF.Property<NpgsqlTypes.NpgsqlTsVector>(d, "ContentVector").Matches(EF.Functions.WebSearchToTsQuery("german", query))
                    || EF.Functions.ILike(d.ContentText!, pattern)))
            .Select(d => new
            {
                d.Id,
                d.Title,
                d.SearchText,
                ObjectId = (from l in db.DocumentObjects
                            join o in db.Objects on l.ObjectId equals o.Id
                            where l.DocumentId == d.Id && visibleObjects.Contains(o.Id)
                            orderby o.Title
                            select (Guid?)o.Id).FirstOrDefault(),
                ObjectTitle = (from l in db.DocumentObjects
                               join o in db.Objects on l.ObjectId equals o.Id
                               where l.DocumentId == d.Id && visibleObjects.Contains(o.Id)
                               orderby o.Title
                               select o.Title).FirstOrDefault(),
                Rank = EF.Property<NpgsqlTypes.NpgsqlTsVector>(d, "SearchVector").Rank(EF.Functions.WebSearchToTsQuery("german", query))
                    + SearchFunctions.WordSimilarity(query, d.SearchText ?? string.Empty)
                    + (EF.Property<NpgsqlTypes.NpgsqlTsVector>(d, "ContentVector").Rank(EF.Functions.WebSearchToTsQuery("german", query)) / 2),
            })
            .OrderByDescending(d => d.Rank).ThenBy(d => d.Title).Take(Limit).ToListAsync(ct);

        // Fundstelle im erkannten Text nur für die Treffer, nicht für alle Dokumente laden.
        var ids = documents.Where(d => Snippet(d.SearchText, query) is null).Select(d => d.Id).ToList();
        var contents = await db.Documents.Where(d => ids.Contains(d.Id)).Select(d => new { d.Id, d.ContentText }).ToDictionaryAsync(d => d.Id, d => d.ContentText, ct);

        if (caller.AdminView)
        {
            var touched = objects.Select(o => o.Id).Concat(documents.Where(d => d.ObjectId != null).Select(d => d.ObjectId!.Value)).Distinct().ToList();
            await access.RecordForeignAccessAsync(caller, await db.Objects.Where(o => touched.Contains(o.Id)).ToListAsync(ct), "search", ct);
        }

        var hits = objects.Select(o => (o.Rank, Hit: new SearchHitDto(SearchKinds.Entry, o.Id, o.Title, o.TypeKey, null, null, Snippet(o.SearchText, query))))
            .Concat(documents.Select(d => (d.Rank, Hit: new SearchHitDto(SearchKinds.Document, d.Id, d.Title, null, d.ObjectId, d.ObjectTitle,
                Snippet(d.SearchText, query) ?? ContentSnippet(contents.GetValueOrDefault(d.Id), query)))))
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

    /// <summary>
    /// Stelle im erkannten Text: das erste Vorkommen eines Suchworts, bei Wortstämmen auch seines Anfangs, mit etwas
    /// Umgebung auf einer Zeile.
    /// </summary>
    internal static string? ContentSnippet(string? text, string query)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.Trim('"', '-', '+')).Where(w => w.Length >= 2).ToList();
        var candidates = words.Concat(words.Where(w => w.Length > 5).Select(w => w[..(w.Length - 2)]));
        var index = candidates.Select(w => text.IndexOf(w, StringComparison.CurrentCultureIgnoreCase)).Where(i => i >= 0).DefaultIfEmpty(-1).Min();
        if (index < 0)
        {
            return null;
        }

        var start = Math.Max(0, index - 60);
        var end = Math.Min(text.Length, index + 100);
        var snippet = string.Join(' ', text[start..end].Split((char[])['\n', '\r', '\t', ' '], StringSplitOptions.RemoveEmptyEntries));
        return (start > 0 ? "…" : string.Empty) + snippet + (end < text.Length ? "…" : string.Empty);
    }
}
