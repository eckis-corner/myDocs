using Microsoft.EntityFrameworkCore;
using MyDocs.Server.Data;

namespace MyDocs.Server.Infrastructure.Search;

/// <summary>Hält den Suchtext aktuell und berechnet beim Start fehlende Einträge nach (etwa nach einem Update).</summary>
internal static class SearchIndex
{
    public static void Update(DocObject item) => item.SearchText = SearchText.For(item);

    public static async Task UpdateAsync(MyDocsDbContext db, Document document, CancellationToken ct)
    {
        var names = await db.DocumentVersions.Where(v => v.DocumentId == document.Id).Select(v => v.FileName).ToListAsync(ct);
        names.AddRange(db.ChangeTracker.Entries<DocumentVersion>().Where(e => e.State == EntityState.Added && e.Entity.DocumentId == document.Id).Select(e => e.Entity.FileName));
        document.SearchText = SearchText.For(document, names);
    }

    public static async Task BackfillAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MyDocsDbContext>();
        foreach (var item in await db.Objects.Where(o => o.SearchText == null).ToListAsync(ct))
        {
            Update(item);
        }

        foreach (var document in await db.Documents.Where(d => d.SearchText == null).ToListAsync(ct))
        {
            await UpdateAsync(db, document, ct);
        }

        await db.SaveChangesAsync(ct);
    }
}
