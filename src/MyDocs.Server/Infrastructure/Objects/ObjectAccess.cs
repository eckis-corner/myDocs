using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MyDocs.Server.Data;
using MyDocs.Shared.Household;

namespace MyDocs.Server.Infrastructure.Objects;

/// <summary>Wer fragt: Konto, Person und ob Mitglied (32).</summary>
public sealed record Caller(Guid UserId, Guid? PersonId, bool IsMember);

/// <summary>
/// Rechte an Objekten, geprüft in jeder Abfrage (26): Haushaltsobjekte sehen alle, ändern Mitglieder; private Objekte
/// sieht und ändert der Eigentümer, dazu wer eine Freigabe hat. Fremde private Objekte tauchen nirgends auf, auch
/// nicht als Anzahl.
/// </summary>
public sealed class ObjectAccess(MyDocsDbContext db)
{
    public async Task<Caller> CallerAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var personId = await db.Persons.Where(p => p.UserId == userId).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
        return new Caller(userId, personId, principal.IsInRole(HouseholdRoles.Member));
    }

    /// <summary>Alle Objekte, die der Aufrufer sehen darf (ohne Papierkorb, wenn nicht ausdrücklich verlangt).</summary>
    public IQueryable<DocObject> Visible(Caller caller, bool deleted = false) =>
        db.Objects.Where(o => o.IsDeleted == deleted
            && (o.OwnerPersonId == null
                || (caller.PersonId != null && o.OwnerPersonId == caller.PersonId)
                || db.ObjectShares.Any(s => s.ObjectId == o.Id && s.UserId == caller.UserId)));

    /// <summary>Darf der Aufrufer den Inhalt ändern?</summary>
    public async Task<bool> CanEditAsync(Caller caller, DocObject item, CancellationToken ct) =>
        item.OwnerPersonId is null
            ? caller.IsMember
            : item.OwnerPersonId == caller.PersonId || await db.ObjectShares.AnyAsync(s => s.ObjectId == item.Id && s.UserId == caller.UserId && s.CanEdit, ct);

    /// <summary>
    /// Darf der Aufrufer Eigentum, Freigaben und Papierkorb verwalten? Bei privaten Objekten der Eigentümer, bei
    /// Haushaltsobjekten jedes Mitglied.
    /// </summary>
    public static bool CanManage(Caller caller, DocObject item) =>
        item.OwnerPersonId is null ? caller.IsMember : item.OwnerPersonId == caller.PersonId;
}
