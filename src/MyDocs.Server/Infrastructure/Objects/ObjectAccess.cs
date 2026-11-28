using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Audit;
using MyDocs.Shared.Audit;
using MyDocs.Shared.Household;

namespace MyDocs.Server.Infrastructure.Objects;

/// <summary>Wer fragt: Konto, Person, ob Mitglied (32) und ob die Admin-Einsicht eingeschaltet ist (31).</summary>
public sealed record Caller(Guid UserId, Guid? PersonId, bool IsMember, bool AdminView = false);

/// <summary>
/// Rechte an Objekten, geprüft in jeder Abfrage (26): Haushaltsobjekte sehen alle, ändern Mitglieder; private Objekte
/// sieht und ändert der Eigentümer, dazu wer eine Freigabe hat. Fremde private Objekte tauchen nirgends auf, auch nicht
/// als Anzahl, außer in der Admin-Einsicht (31): Dann sieht der Administrator alles, ändert aber nichts, und jeder
/// Zugriff erscheint im Protokoll des Eigentümers.
/// </summary>
public sealed class ObjectAccess(MyDocsDbContext db, IHttpContextAccessor http, AuditLog audit, TimeProvider time)
{
    /// <summary>Heute nach der Uhr des Servers (in Tests einstellbar).</summary>
    public DateOnly Today => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);

    /// <summary>Jetzt nach der Uhr des Servers.</summary>
    public DateTimeOffset Now => time.GetUtcNow();

    public async Task<Caller> CallerAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        var userId = Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var personId = await db.Persons.Where(p => p.UserId == userId).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
        var adminView = principal.IsInRole(HouseholdRoles.Admin)
            && http.HttpContext?.Request.Headers[AdminViewHeader.Name].ToString() == AdminViewHeader.Value;
        return new Caller(userId, personId, principal.IsInRole(HouseholdRoles.Member), adminView);
    }

    /// <summary>Alle Objekte, die der Aufrufer sehen darf (ohne Papierkorb, wenn nicht ausdrücklich verlangt).</summary>
    public IQueryable<DocObject> Visible(Caller caller, bool deleted = false) =>
        caller.AdminView
            ? db.Objects.Where(o => o.IsDeleted == deleted)
            : db.Objects.Where(o => o.IsDeleted == deleted
                && (o.OwnerPersonId == null
                    || (caller.PersonId != null && o.OwnerPersonId == caller.PersonId)
                    || db.ObjectShares.Any(s => s.ObjectId == o.Id && s.UserId == caller.UserId)));

    /// <summary>Darf der Aufrufer den Inhalt ändern? Die Admin-Einsicht gibt kein Recht zum Ändern.</summary>
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

    /// <summary>Fremdes privates Objekt: gehört einer anderen Person.</summary>
    public static bool IsForeignPrivate(Caller caller, DocObject item) => item.OwnerPersonId is not null && item.OwnerPersonId != caller.PersonId;

    /// <summary>
    /// Protokolliert, wenn jemand fremde private Objekte ansieht: über eine Freigabe als „angesehen“, über die
    /// Admin-Einsicht als „Admin-Einsicht“. Der Eigentümer sieht den Eintrag in seinem Protokoll (31, 35).
    /// </summary>
    public async Task RecordForeignAccessAsync(Caller caller, IEnumerable<DocObject> items, string? context, CancellationToken ct)
    {
        var foreign = items.Where(i => IsForeignPrivate(caller, i)).ToList();
        if (foreign.Count == 0)
        {
            return;
        }

        var ids = foreign.Select(i => i.Id).ToList();
        var shared = await db.ObjectShares.Where(s => s.UserId == caller.UserId && ids.Contains(s.ObjectId)).Select(s => s.ObjectId).ToListAsync(ct);
        foreach (var item in foreign)
        {
            var viaShare = shared.Contains(item.Id);
            if (viaShare && context is not null)
            {
                // Listen und Suche über eine Freigabe sind erwartet; protokolliert wird das Öffnen.
                continue;
            }

            audit.Add(viaShare ? AuditActions.ObjectViewed : AuditActions.AdminView, caller.UserId, item.OwnerPersonId, item.Id, title: item.Title, details: context);
        }

        await db.SaveChangesAsync(ct);
    }
}
