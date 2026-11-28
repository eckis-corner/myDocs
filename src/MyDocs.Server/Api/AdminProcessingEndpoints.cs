using Microsoft.EntityFrameworkCore;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Auth;
using MyDocs.Server.Infrastructure.Processing;
using MyDocs.Shared.Admin;
using MyDocs.Shared.Documents;

namespace MyDocs.Server.Api;

/// <summary>Verarbeitung im Worker (24) für Administratoren: nur Zahlen, keine Dateinamen.</summary>
internal static class AdminProcessingEndpoints
{
    public static IEndpointRouteBuilder MapAdminProcessingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/processing").RequireAuthorization(Roles.Admin);
        group.MapGet("", StatusAsync);
        group.MapPost("/retry", RetryAsync);
        return app;
    }

    private static async Task<IResult> StatusAsync(MyDocsDbContext db, IDocumentWorker worker, MyDocs.Server.Infrastructure.Inbox.ScanFolderService scan, CancellationToken ct)
    {
        var counts = await db.ProcessingJobs.GroupBy(j => j.State).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        return TypedResults.Ok(new ProcessingStatusDto(worker.IsConfigured, await worker.IsHealthyAsync(ct),
            counts.GetValueOrDefault(ProcessingStates.Pending), counts.GetValueOrDefault(ProcessingStates.Failed), counts.GetValueOrDefault(ProcessingStates.Done))
        {
            ScanFolderActive = scan.IsActive,
        });
    }

    private static async Task<IResult> RetryAsync(ProcessingScheduler queue, CancellationToken ct) => TypedResults.Ok(await queue.RetryFailedAsync(ct));
}
