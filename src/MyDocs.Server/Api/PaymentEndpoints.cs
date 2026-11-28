using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MyCommon.Catalog;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Objects;
using MyDocs.Shared.Objects;
using MyDocs.Shared.Payments;

namespace MyDocs.Server.Api;

/// <summary>
/// Übersicht „Zahlungen“ (62, 173): alle laufenden regelmäßigen Zahlungen, die der Aufrufer sehen darf. „Meins“ heißt
/// wie auf der Startseite: zuständig oder Eigentümer; „Haushalt“ zeigt alles Sichtbare mit der zuständigen Person.
/// Gekündigte Einträge laufen bis zu ihrem Datum mit und tragen es als Ende.
/// </summary>
internal static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/payments", ListAsync).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> ListAsync(string? scope, ClaimsPrincipal principal, ObjectAccess access, MyDocsDbContext db, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        var objects = await access.Visible(caller).ToListAsync(ct);
        if (scope != "household")
        {
            objects = [.. objects.Where(o => o.ResponsiblePersonId == caller.PersonId || o.OwnerPersonId == caller.PersonId)];
        }

        var today = access.Today;
        var rows = objects
            .Select(o => (Item: o, Payment: Payments.Of(o)))
            .Where(r => r.Payment is not null && !(r.Item.LifecycleState == LifecycleStates.Cancelled && r.Item.LifecycleDate < today))
            .ToList();
        await access.RecordForeignAccessAsync(caller, rows.Select(r => r.Item), "payments", ct);

        var accountIds = rows.Select(r => r.Payment!.AccountId).OfType<Guid>().Distinct().ToList();
        var accounts = await access.Visible(caller).Where(o => accountIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Title, ct);
        var personIds = rows.Select(r => r.Item.ResponsiblePersonId ?? r.Item.OwnerPersonId).OfType<Guid>().Distinct().ToList();
        var persons = await db.Persons.Where(p => personIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.DisplayName, ct);

        var result = rows.Select(r =>
        {
            var (item, payment) = (r.Item, r.Payment!);
            var responsible = item.ResponsiblePersonId ?? item.OwnerPersonId;
            return new PaymentDto(item.Id, item.Title, item.TypeKey, ObjectCatalog.Find(item.TypeKey)!.Area, payment.Amount, payment.Frequency,
                payment.AccountId is { } account && accounts.ContainsKey(account) ? account : null,
                payment.AccountId is { } id ? accounts.GetValueOrDefault(id) : null,
                responsible, responsible is { } person ? persons.GetValueOrDefault(person) : null, item.OwnerPersonId is null,
                item.LifecycleState == LifecycleStates.Cancelled ? item.LifecycleDate : null);
        }).OrderBy(p => p.Area).ThenBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        return TypedResults.Ok(result);
    }
}
