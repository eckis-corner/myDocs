using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyCommon.Catalog;
using MyDocs.Server.Data;
using MyDocs.Shared.MyLife;
using MyDocs.Shared.Objects;

namespace MyDocs.Server.Infrastructure.MyLife;

/// <summary>
/// Brücke zu myLife (56–63, 162–166): Was geht an wessen Tresor, wie sieht der Stand aus, und wer fragt mit welchem
/// Token. myLife zieht; myDocs liefert nur Klartext über TLS und merkt sich den quittierten Stand.
/// </summary>
public sealed class MyLifeBridge(MyDocsDbContext db, UserManager<AppUser> users, TimeProvider time)
{
    /// <summary>Zustände, die in myLife „beendet“ heißen (29).</summary>
    private static readonly HashSet<string> EndedStates = new(StringComparer.Ordinal)
    {
        LifecycleStates.Ended, LifecycleStates.Archived, LifecycleStates.Expired, LifecycleStates.Replaced, LifecycleStates.Disposed,
    };

    private static readonly JsonSerializerOptions FingerprintJson = new(JsonSerializerDefaults.Web);

    /// <summary>Neues lesendes Token: Präfix und 256 Bit Zufall, base64url.</summary>
    public static string NewToken() => MyLifeRules.TokenPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    /// <summary>
    /// Prüft <c>Authorization: Bearer mdl_…</c>. Gilt nur, solange das Konto besteht, nicht gesperrt ist und eine Person
    /// hat. Das Token taugt für nichts anderes als die Endpunkte unter <c>/api/mylife</c>.
    /// </summary>
    public async Task<(MyLifeLink Link, Person Person)?> AuthenticateAsync(HttpRequest request, CancellationToken ct)
    {
        var header = request.Headers.Authorization.ToString();
        const string bearer = "Bearer ";
        if (!header.StartsWith(bearer, StringComparison.Ordinal) || header[bearer.Length..].Trim() is not { } token
            || !token.StartsWith(MyLifeRules.TokenPrefix, StringComparison.Ordinal) || token.Length > 100)
        {
            return null;
        }

        var hash = Hash(token);
        if (await db.MyLifeLinks.FirstOrDefaultAsync(l => l.TokenHash == hash, ct) is not { } link
            || await users.FindByIdAsync(link.UserId.ToString()) is not { } user || user.LockoutEnd > time.GetUtcNow()
            || await db.Persons.FirstOrDefaultAsync(p => p.UserId == link.UserId && !p.IsDeleted, ct) is not { } person)
        {
            return null;
        }

        var now = time.GetUtcNow();
        if (link.LastUsedAt is not { } used || now - used > TimeSpan.FromMinutes(5))
        {
            link.LastUsedAt = now;
            await db.SaveChangesAsync(ct);
        }

        return (link, person);
    }

    /// <summary>
    /// Einträge für den Tresor einer Person (163): eigene private und Haushaltseinträge, für die sie zuständig ist,
    /// jeweils mit „In myLife führen“.
    /// </summary>
    public IQueryable<DocObject> Scope(Guid personId) =>
        db.Objects.Where(o => !o.IsDeleted && o.MyLifeSync
            && (o.OwnerPersonId == personId || (o.OwnerPersonId == null && o.ResponsiblePersonId == personId)));

    /// <summary>Der aktuelle Stand für myLife, mit Fingerabdruck über den Inhalt.</summary>
    public async Task<BridgeExport> BuildAsync(Person person, string instanceUrl, CancellationToken ct)
    {
        var items = (await Scope(person.Id).ToListAsync(ct)).Where(o => MyDocsCatalog.Find(o.TypeKey) is not null).ToList();

        // Ablageorte, auf die diese Einträge verweisen, kommen mit, auch wenn jemand anderes für sie zuständig ist (63).
        var ids = items.Select(i => i.Id).ToHashSet();
        var wanted = items.SelectMany(i => i.Locations.Values).Where(id => !ids.Contains(id)).Distinct().ToList();
        if (wanted.Count > 0)
        {
            items.AddRange(await db.Objects.Where(o => wanted.Contains(o.Id) && !o.IsDeleted && o.MyLifeSync && o.TypeKey == ObjectCatalog.LocationType
                && (o.OwnerPersonId == null || o.OwnerPersonId == person.Id)).ToListAsync(ct));
            ids = items.Select(i => i.Id).ToHashSet();
        }

        var documents = await DocumentsAsync(ids, ct);
        var objects = items.OrderBy(i => i.Id).Select(item => ToBridge(item, ids, documents.GetValueOrDefault(item.Id) ?? [])).ToList();
        return new BridgeExport(BridgeExport.FormatName, BridgeExport.CurrentVersion, time.GetUtcNow(), Fingerprint(objects),
            new BridgeInstance(instanceUrl), new BridgePerson(person.Id, person.DisplayName), objects);
    }

    /// <summary>Ist das Dokument für diese Person mit einem übertragenen Eintrag markiert? Dann die aktuelle Version.</summary>
    public async Task<DocumentVersion?> DocumentAsync(Person person, Guid documentId, CancellationToken ct)
    {
        var scope = Scope(person.Id).Select(o => o.Id);
        var marked = await db.Documents.AnyAsync(d => d.Id == documentId && d.ToMyLife && !d.IsDeleted && !d.InInbox
            && db.DocumentObjects.Any(l => l.DocumentId == d.Id && scope.Contains(l.ObjectId)), ct);
        return marked
            ? await db.DocumentVersions.AsNoTracking().Where(v => v.DocumentId == documentId).OrderByDescending(v => v.Number).FirstOrDefaultAsync(ct)
            : null;
    }

    /// <summary>
    /// Fingerabdruck (164): SHA-256 über die Einträge ohne Zeitstempel. Eine Notiz in myDocs ändert ihn nicht, eine neue
    /// Fassung eines markierten Dokuments schon.
    /// </summary>
    public static string Fingerprint(IEnumerable<BridgeObject> objects)
    {
        var content = objects.OrderBy(o => o.Id).Select(o => o with { UpdatedAt = default }).ToList();
        return Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(content, FingerprintJson)));
    }

    /// <summary>
    /// Fortschreibung für den Vorgang „myLife abgleichen“ (60, 165): je Verbindung, ob der Stand vom quittierten
    /// abweicht und seit wann. Liefert die Verbindungen, die seit 30 Tagen offen sind, mit ihrer Person.
    /// </summary>
    public async Task<IReadOnlyList<(MyLifeLink Link, Person Person)>> RefreshPendingAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var due = new List<(MyLifeLink, Person)>();
        foreach (var link in await db.MyLifeLinks.ToListAsync(ct))
        {
            if (await db.Persons.FirstOrDefaultAsync(p => p.UserId == link.UserId && !p.IsDeleted, ct) is not { } person)
            {
                continue;
            }

            var current = (await BuildAsync(person, string.Empty, ct)).Fingerprint;
            if (current == link.SyncedFingerprint)
            {
                link.PendingSince = null;
                continue;
            }

            // Nie abgeglichen: Die Frist läuft ab dem Verbinden.
            link.PendingSince ??= link.LastSyncedAt is null ? link.CreatedAt : now;
            if (now - link.PendingSince.Value >= TimeSpan.FromDays(MyLifeRules.SyncDueDays))
            {
                due.Add((link, person));
            }
        }

        return due;
    }

    private async Task<Dictionary<Guid, List<BridgeDocument>>> DocumentsAsync(HashSet<Guid> objectIds, CancellationToken ct)
    {
        var rows = await (from l in db.DocumentObjects
                          join d in db.Documents on l.DocumentId equals d.Id
                          where objectIds.Contains(l.ObjectId) && d.ToMyLife && !d.IsDeleted && !d.InInbox
                          select new { l.ObjectId, d.Id, d.Title, d.DocumentDate }).ToListAsync(ct);
        var documentIds = rows.Select(r => r.Id).Distinct().ToList();
        var versions = (await db.DocumentVersions.AsNoTracking().Where(v => documentIds.Contains(v.DocumentId)).ToListAsync(ct))
            .GroupBy(v => v.DocumentId).ToDictionary(g => g.Key, g => g.MaxBy(v => v.Number)!);
        return rows.Where(r => versions.ContainsKey(r.Id))
            .GroupBy(r => r.ObjectId)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.Id).Select(r =>
            {
                var v = versions[r.Id];
                return new BridgeDocument(r.Id, v.BlobHash, r.Title, v.FileName, v.ContentType, v.Size, r.DocumentDate);
            }).ToList());
    }

    private static BridgeObject ToBridge(DocObject item, HashSet<Guid> exported, List<BridgeDocument> documents)
    {
        var type = ObjectCatalog.Find(item.TypeKey);
        var fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in item.Fields)
        {
            // Verweise nur auf Einträge, die mitkommen; alles andere wäre in myLife ein toter Verweis.
            if (type?.Field(key) is { Type: FieldType.Reference } && (!Guid.TryParse(value, out var target) || !exported.Contains(target)))
            {
                continue;
            }

            fields[key] = value;
        }

        var locations = new SortedDictionary<string, Guid>(item.Locations.Where(l => exported.Contains(l.Value)).ToDictionary(l => l.Key, l => l.Value), StringComparer.Ordinal);
        return new BridgeObject(item.Id, item.TypeKey, item.SchemaVersion, item.Title, fields, locations, item.ExpiresOn,
            item.LifecycleState, item.LifecycleDate, item.LifecycleState is { } state && EndedStates.Contains(state),
            item.LastReviewedOn, item.OverdueSince, item.UpdatedAt, documents);
    }
}
