using MyDocs.Server.Api;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Audit;
using MyDocs.Server.Infrastructure.Processing;
using MyDocs.Server.Infrastructure.Search;
using MyDocs.Shared.Audit;

namespace MyDocs.Server.Infrastructure.Inbox;

/// <summary>
/// Legt ein Dokument im Posteingang an, gleich aus welchem Kanal (19): Upload ohne Eintrag, Kamera, Scan-Ordner, Mail.
/// Die Datei liegt schon verschlüsselt als Blob; gespeichert wird mit dem nächsten <c>SaveChanges</c> des Aufrufers.
/// </summary>
public static class InboxIntake
{
    public static async Task<Document> AddAsync(MyDocsDbContext db, ProcessingScheduler processing, AuditLog audit, string hash, long size,
        string fileName, string contentType, Guid? personId, string source, string? sourceDetail, Guid actor, DateTimeOffset now, CancellationToken ct, string? title = null)
    {
        var document = new Document
        {
            Id = Guid.CreateVersion7(),
            Title = title is { Length: > 0 } ? title[..Math.Min(title.Length, 200)] : DocumentEndpoints.TitleFrom(fileName),
            CreatedAt = now,
            CreatedBy = actor,
            UpdatedAt = now,
            InInbox = true,
            InboxPersonId = personId,
            Source = source,
            SourceDetail = sourceDetail is { Length: > 300 } ? sourceDetail[..300] : sourceDetail,
        };
        db.Documents.Add(document);
        db.DocumentVersions.Add(DocumentEndpoints.Version(document.Id, 1, new DocumentEndpoints.Upload(fileName, contentType, hash, size, null), actor, now));
        await processing.EnqueueAsync(hash, fileName, ct);
        document.ContentText = await processing.TextAsync(hash, ct);
        await SearchIndex.UpdateAsync(db, document, ct);
        audit.Add(AuditActions.DocumentUploaded, actor, personId, documentId: document.Id, title: document.Title, details: $"{source}: {fileName}");
        return document;
    }
}
