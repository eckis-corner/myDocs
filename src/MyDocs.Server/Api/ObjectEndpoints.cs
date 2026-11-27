using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MyCommon.Catalog;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Audit;
using MyDocs.Server.Infrastructure.Objects;
using MyDocs.Server.Infrastructure.Tasks;
using MyDocs.Shared.Audit;
using MyDocs.Shared.Objects;

namespace MyDocs.Server.Api;

/// <summary>Objekte aus dem Katalog: anlegen, lesen, ändern, Papierkorb, Freigaben, Historie (M1.4).</summary>
internal static class ObjectEndpoints
{
    public static IEndpointRouteBuilder MapObjectEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/objects").RequireAuthorization();
        group.MapGet("/", ListAsync);
        group.MapGet("/trash", TrashAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("/", CreateAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapDelete("/{id:guid}", DeleteAsync);
        group.MapPost("/{id:guid}/restore", RestoreAsync);
        group.MapPut("/{id:guid}/shares", SharesAsync);
        group.MapGet("/{id:guid}/history", HistoryAsync);
        group.MapPost("/{id:guid}/lifecycle", LifecycleAsync);
        group.MapPut("/{id:guid}/review", ReviewSettingsAsync);
        group.MapPost("/{id:guid}/review/now", ReviewNowAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(int? area, string? type, bool? archived, ClaimsPrincipal principal, ObjectAccess access, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        // Archivierte Einträge verschwinden aus den Listen (29), bleiben aber über die Suche und den Schalter „Archiv“ erreichbar.
        var query = archived == true
            ? access.Visible(caller).Where(o => o.LifecycleState == LifecycleStates.Archived)
            : access.Visible(caller).Where(o => o.LifecycleState == null || o.LifecycleState != LifecycleStates.Archived);
        if (type is not null)
        {
            query = query.Where(o => o.TypeKey == type);
        }
        else if (area is { } number)
        {
            var keys = MyDocsCatalog.TypesIn(number).Select(t => t.Key).ToList();
            query = query.Where(o => keys.Contains(o.TypeKey));
        }

        var items = await query.OrderBy(o => o.Title).ToListAsync(ct);
        await access.RecordForeignAccessAsync(caller, items, "list", ct);
        return TypedResults.Ok(items.Select(Summary).ToList());
    }

    private static async Task<IResult> TrashAsync(ClaimsPrincipal principal, ObjectAccess access, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        var items = await access.Visible(caller, deleted: true).OrderByDescending(o => o.DeletedAt).ToListAsync(ct);
        return TypedResults.Ok(items.Where(o => ObjectAccess.CanManage(caller, o)).Select(Summary).ToList());
    }

    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal principal, ObjectAccess access, MyDocsDbContext db, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        var item = await access.Visible(caller).FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? await access.Visible(caller, deleted: true).FirstOrDefaultAsync(o => o.Id == id, ct);
        if (item is null)
        {
            return NotFound();
        }

        await access.RecordForeignAccessAsync(caller, [item], null, ct);
        return TypedResults.Ok(await ToDtoAsync(item, caller, access, db, ct));
    }

    private static async Task<IResult> CreateAsync(ObjectSaveRequest request, ClaimsPrincipal principal, ObjectAccess access,
        MyDocsDbContext db, AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (MyDocsCatalog.Find(request.TypeKey) is not { } type)
        {
            return ApiProblems.Of(ObjectCatalog.Find(request.TypeKey) is null ? ObjectErrors.TypeUnknown : ObjectErrors.TypeOnlyInMyLife);
        }

        // Neu anlegen dürfen Mitglieder; privat nur für sich selbst.
        if (!caller.IsMember)
        {
            return Forbidden();
        }

        if (request.OwnerPersonId is { } owner && owner != caller.PersonId)
        {
            return ApiProblems.Of(ObjectErrors.OwnerInvalid);
        }

        if (await ValidateAsync(type, request, db, ct) is { } problem)
        {
            return problem;
        }

        var now = time.GetUtcNow();
        var item = new DocObject
        {
            Id = Guid.CreateVersion7(),
            TypeKey = type.Key,
            SchemaVersion = type.SchemaVersion,
            CreatedAt = now,
            CreatedBy = caller.UserId,
            UpdatedAt = now,
            OwnerPersonId = request.OwnerPersonId,
            ResponsiblePersonId = request.ResponsiblePersonId,
            LifecycleState = Lifecycle.InitialState(type.Key),
        };
        Apply(item, request.Data);
        db.Objects.Add(item);
        audit.Add(AuditActions.ObjectCreated, caller.UserId, item.OwnerPersonId, item.Id, title: item.Title);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ToDtoAsync(item, caller, access, db, ct));
    }

    private static async Task<IResult> UpdateAsync(Guid id, ObjectSaveRequest request, ClaimsPrincipal principal, ObjectAccess access,
        MyDocsDbContext db, AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (await access.Visible(caller).FirstOrDefaultAsync(o => o.Id == id, ct) is not { } item)
        {
            return NotFound();
        }

        if (!await access.CanEditAsync(caller, item, ct))
        {
            return Forbidden();
        }

        if (request.TypeKey != item.TypeKey || MyDocsCatalog.Find(item.TypeKey) is not { } type)
        {
            return ApiProblems.Of(ObjectErrors.TypeUnknown);
        }

        // Eigentum wechseln (34): privat → Haushalt durch den Eigentümer, Haushalt → privat nur durch die zuständige
        // Person und nur für sich selbst. Wer nur eine Freigabe hat, ändert das Eigentum nicht.
        if (request.OwnerPersonId != item.OwnerPersonId)
        {
            var allowed = item.OwnerPersonId is null
                ? request.OwnerPersonId == caller.PersonId && item.ResponsiblePersonId == caller.PersonId
                : item.OwnerPersonId == caller.PersonId && request.OwnerPersonId is null && caller.IsMember;
            if (!allowed)
            {
                return ApiProblems.Of(ObjectErrors.OwnerInvalid);
            }
        }

        if (await ValidateAsync(type, request, db, ct) is { } problem)
        {
            return problem;
        }

        db.Entry(item).Property(o => o.Version).OriginalValue = request.Version;
        var previousOwner = item.OwnerPersonId;
        var before = Snapshot(item);
        item.OwnerPersonId = request.OwnerPersonId;
        item.ResponsiblePersonId = request.ResponsiblePersonId;
        Apply(item, request.Data);
        item.UpdatedAt = time.GetUtcNow();
        var changes = AddHistory(db, item, before, Snapshot(item), caller.UserId, item.UpdatedAt);
        audit.Add(AuditActions.ObjectChanged, caller.UserId, item.OwnerPersonId ?? previousOwner, item.Id, title: item.Title, details: string.Join(", ", changes));
        if (item.OwnerPersonId is null)
        {
            // Im Haushalt gibt es keine Freigaben; sie gelten nur für private Objekte.
            db.ObjectShares.RemoveRange(await db.ObjectShares.Where(s => s.ObjectId == item.Id).ToListAsync(ct));
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiProblems.Of(ObjectErrors.Conflict, StatusCodes.Status409Conflict);
        }

        return TypedResults.Ok(await ToDtoAsync(item, caller, access, db, ct));
    }

    private static async Task<IResult> DeleteAsync(Guid id, ClaimsPrincipal principal, ObjectAccess access, MyDocsDbContext db, AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (await access.Visible(caller).FirstOrDefaultAsync(o => o.Id == id, ct) is not { } item)
        {
            return NotFound();
        }

        if (!ObjectAccess.CanManage(caller, item))
        {
            return Forbidden();
        }

        item.IsDeleted = true;
        item.DeletedAt = time.GetUtcNow();
        AddHistory(db, item, new() { ["deleted"] = null }, new() { ["deleted"] = "true" }, caller.UserId, item.DeletedAt.Value);
        audit.Add(AuditActions.ObjectDeleted, caller.UserId, item.OwnerPersonId, item.Id, title: item.Title);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> RestoreAsync(Guid id, ClaimsPrincipal principal, ObjectAccess access, MyDocsDbContext db, AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (await access.Visible(caller, deleted: true).FirstOrDefaultAsync(o => o.Id == id, ct) is not { } item)
        {
            return NotFound();
        }

        if (!ObjectAccess.CanManage(caller, item))
        {
            return Forbidden();
        }

        item.IsDeleted = false;
        item.DeletedAt = null;
        AddHistory(db, item, new() { ["deleted"] = "true" }, new() { ["deleted"] = null }, caller.UserId, time.GetUtcNow());
        audit.Add(AuditActions.ObjectRestored, caller.UserId, item.OwnerPersonId, item.Id, title: item.Title);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ToDtoAsync(item, caller, access, db, ct));
    }

    private static async Task<IResult> SharesAsync(Guid id, List<ObjectShareDto> shares, ClaimsPrincipal principal, ObjectAccess access,
        MyDocsDbContext db, AuditLog audit, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (await access.Visible(caller).FirstOrDefaultAsync(o => o.Id == id, ct) is not { } item)
        {
            return NotFound();
        }

        // Freigaben gibt es nur für private Objekte, verwaltet vom Eigentümer (33).
        if (item.OwnerPersonId is null || item.OwnerPersonId != caller.PersonId)
        {
            return Forbidden();
        }

        var wanted = shares.Where(s => s.UserId != caller.UserId).DistinctBy(s => s.UserId).ToList();
        var known = await db.Users.Where(u => wanted.Select(w => w.UserId).Contains(u.Id)).Select(u => u.Id).ToListAsync(ct);
        db.ObjectShares.RemoveRange(await db.ObjectShares.Where(s => s.ObjectId == id).ToListAsync(ct));
        db.ObjectShares.AddRange(wanted.Where(w => known.Contains(w.UserId)).Select(w => new ObjectShare { ObjectId = id, UserId = w.UserId, CanEdit = w.CanEdit }));
        var names = await db.Users.Where(u => known.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        audit.Add(AuditActions.ObjectShared, caller.UserId, item.OwnerPersonId, item.Id, title: item.Title,
            details: string.Join(", ", wanted.Where(w => names.ContainsKey(w.UserId)).Select(w => $"{names[w.UserId]}: {(w.CanEdit ? "bearbeiten" : "lesen")}")));
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ToDtoAsync(item, caller, access, db, ct));
    }

    private static async Task<IResult> HistoryAsync(Guid id, ClaimsPrincipal principal, ObjectAccess access, MyDocsDbContext db, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        var item = await access.Visible(caller).FirstOrDefaultAsync(o => o.Id == id, ct) ?? await access.Visible(caller, deleted: true).FirstOrDefaultAsync(o => o.Id == id, ct);
        if (item is null)
        {
            return NotFound();
        }

        await access.RecordForeignAccessAsync(caller, [item], "history", ct);

        var entries = await (from h in db.ObjectHistory
                             join u in db.Users on h.ActorId equals u.Id into actors
                             from actor in actors.DefaultIfEmpty()
                             where h.ObjectId == id
                             orderby h.At descending, h.Id descending
                             select new ObjectHistoryDto(h.At, actor != null ? actor.DisplayName : "?", h.Field, h.OldValue, h.NewValue)).Take(500).ToListAsync(ct);
        return TypedResults.Ok(entries);
    }

    /// <summary>
    /// Wechsel im Lebenszyklus (9, 10): nur erlaubte Wechsel des Musters, mit Datum, wo es eins braucht, und mit
    /// Nachfolger bei „ersetzt“. Legt die Folgevorgänge an.
    /// </summary>
    private static async Task<IResult> LifecycleAsync(Guid id, LifecycleChangeRequest request, ClaimsPrincipal principal, ObjectAccess access,
        MyDocsDbContext db, AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (await access.Visible(caller).FirstOrDefaultAsync(o => o.Id == id, ct) is not { } item)
        {
            return NotFound();
        }

        if (!await access.CanEditAsync(caller, item, ct))
        {
            return Forbidden();
        }

        if (!Lifecycle.CanChange(item.TypeKey, item.LifecycleState, request.State))
        {
            return ApiProblems.Of(ObjectErrors.LifecycleInvalid);
        }

        if (Lifecycle.NeedsDate(request.State) && request.Date is null)
        {
            return ApiProblems.Of(ObjectErrors.LifecycleDateRequired);
        }

        if (request.State == LifecycleStates.Replaced
            && (request.ReplacedById is not { } successor || successor == item.Id
                || !await access.Visible(caller).AnyAsync(o => o.Id == successor && o.TypeKey == item.TypeKey, ct)))
        {
            return ApiProblems.Of(ObjectErrors.ReplacementInvalid);
        }

        db.Entry(item).Property(o => o.Version).OriginalValue = request.Version;
        var now = time.GetUtcNow();
        var before = new Dictionary<string, string?> { ["lifecycle"] = item.LifecycleState, ["lifecycleDate"] = item.LifecycleDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        item.LifecycleState = request.State;
        item.LifecycleDate = Lifecycle.NeedsDate(request.State) ? request.Date : null;
        item.ReplacedById = request.State == LifecycleStates.Replaced ? request.ReplacedById : null;
        item.UpdatedAt = now;
        AddHistory(db, item, before, new() { ["lifecycle"] = item.LifecycleState, ["lifecycleDate"] = item.LifecycleDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }, caller.UserId, now);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        // Beim Abgeben eines Fahrzeugs oder einer Immobilie gehören die Verträge, die darauf verweisen, mit in den Vorgang.
        var related = request.State == LifecycleStates.Disposed
            ? (await access.Visible(caller).Where(o => o.Id != item.Id).ToListAsync(ct)).Where(o => o.Fields.ContainsValue(item.Id.ToString())).Select(o => o.Id).ToList()
            : [];
        FollowUps.Create(db, item, request.State, today, now, caller.UserId, related);
        audit.Add(AuditActions.ObjectChanged, caller.UserId, item.OwnerPersonId, item.Id, title: item.Title, details: $"lifecycle: {request.State}");
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiProblems.Of(ObjectErrors.Conflict, StatusCodes.Status409Conflict);
        }

        return TypedResults.Ok(await ToDtoAsync(item, caller, access, db, ct));
    }

    /// <summary>Eigene Prüfregel (13): Intervall in Monaten (0 = keine, null = Regel des Typs) und fester nächster Termin.</summary>
    private static async Task<IResult> ReviewSettingsAsync(Guid id, ReviewSettingsRequest request, ClaimsPrincipal principal, ObjectAccess access,
        MyDocsDbContext db, TimeProvider time, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (await access.Visible(caller).FirstOrDefaultAsync(o => o.Id == id, ct) is not { } item)
        {
            return NotFound();
        }

        if (!await access.CanEditAsync(caller, item, ct))
        {
            return Forbidden();
        }

        if (request.IntervalMonths is < 0 or > 120)
        {
            return ApiProblems.Of(ObjectErrors.Invalid);
        }

        var before = new Dictionary<string, string?> { ["reviewInterval"] = item.ReviewIntervalMonths?.ToString(CultureInfo.InvariantCulture), ["nextReview"] = item.NextReviewOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        item.ReviewIntervalMonths = request.IntervalMonths;
        item.NextReviewOn = request.NextReviewOn;
        AddHistory(db, item, before, new() { ["reviewInterval"] = item.ReviewIntervalMonths?.ToString(CultureInfo.InvariantCulture), ["nextReview"] = item.NextReviewOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }, caller.UserId, time.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ToDtoAsync(item, caller, access, db, ct));
    }

    /// <summary>„Jetzt prüfen“: legt die Prüfung sofort an, fällig heute; ein offener Vorgang „Prüfung“ wird nicht verdoppelt.</summary>
    private static async Task<IResult> ReviewNowAsync(Guid id, ClaimsPrincipal principal, ObjectAccess access, MyDocsDbContext db, TimeProvider time, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (await access.Visible(caller).FirstOrDefaultAsync(o => o.Id == id, ct) is not { } item)
        {
            return NotFound();
        }

        if (!await access.CanEditAsync(caller, item, ct))
        {
            return Forbidden();
        }

        var rule = $"review:{item.Id}";
        var open = await db.Tasks.FirstOrDefaultAsync(t => t.RuleKey == rule && t.State != MyDocs.Shared.Tasks.TaskStates.Done && t.State != MyDocs.Shared.Tasks.TaskStates.Discarded, ct);
        if (open is null)
        {
            var now = time.GetUtcNow();
            open = new WorkTask
            {
                Id = Guid.CreateVersion7(),
                Kind = MyDocs.Shared.Tasks.TaskKinds.Review,
                Title = "Prüfen, ob alles noch stimmt",
                TitleKey = "review",
                RuleKey = rule,
                ResponsiblePersonId = item.ResponsiblePersonId ?? item.OwnerPersonId ?? caller.PersonId,
                DueOn = DateOnly.FromDateTime(now.UtcDateTime),
                CreatedAt = now,
                CreatedBy = caller.UserId,
            };
            db.Tasks.Add(open);
            db.TaskObjects.Add(new TaskObject { TaskId = open.Id, ObjectId = item.Id });
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(new { taskId = open.Id });
    }

    private static async Task<IResult?> ValidateAsync(ObjectTypeDefinition type, ObjectSaveRequest request, MyDocsDbContext db, CancellationToken ct)
    {
        var errors = MyDocsCatalog.Validate(type, request.Data ?? new ObjectData());
        if (request.OwnerPersonId is null && request.ResponsiblePersonId is null)
        {
            errors["responsible"] = "required";
        }

        if (request.ResponsiblePersonId is { } responsible && !await db.Persons.AnyAsync(p => p.Id == responsible && p.UserId != null, ct))
        {
            errors["responsible"] = "invalid";
        }

        return errors.Count == 0
            ? null
            : TypedResults.BadRequest(new ObjectValidationProblem(ObjectErrors.Invalid, errors));
    }

    private static void Apply(DocObject item, ObjectData data)
    {
        item.Title = data.Title.Trim();
        item.Fields = data.Fields.Where(f => !string.IsNullOrWhiteSpace(f.Value)).ToDictionary(f => f.Key, f => f.Value.Trim(), StringComparer.Ordinal);
        item.Locations = data.Locations.Where(l => item.Fields.ContainsKey(l.Key)).ToDictionary(l => l.Key, l => l.Value, StringComparer.Ordinal);
        item.Notes = string.IsNullOrWhiteSpace(data.Notes) ? null : data.Notes.Trim();
        item.ExpiresOn = data.ExpiresOn;
        Infrastructure.Search.SearchIndex.Update(item);
    }

    /// <summary>Alle vergleichbaren Angaben als Text, für die Historie.</summary>
    private static Dictionary<string, string?> Snapshot(DocObject item)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["title"] = item.Title,
            ["notes"] = item.Notes,
            ["expiresOn"] = item.ExpiresOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["owner"] = item.OwnerPersonId?.ToString(),
            ["responsible"] = item.ResponsiblePersonId?.ToString(),
        };
        foreach (var (key, value) in item.Fields)
        {
            values[$"field:{key}"] = value;
        }

        return values;
    }

    private static List<string> AddHistory(MyDocsDbContext db, DocObject item, Dictionary<string, string?> before, Dictionary<string, string?> after, Guid actor, DateTimeOffset at)
    {
        var changed = new List<string>();
        foreach (var key in before.Keys.Union(after.Keys).Order(StringComparer.Ordinal))
        {
            var old = before.GetValueOrDefault(key);
            var now = after.GetValueOrDefault(key);
            if (old != now)
            {
                db.ObjectHistory.Add(new ObjectHistoryEntry { ObjectId = item.Id, At = at, ActorId = actor, Field = key, OldValue = old, NewValue = now });
                changed.Add(key.StartsWith("field:", StringComparison.Ordinal) ? key[6..] : key);
            }
        }

        return changed;
    }

    private static ObjectSummaryDto Summary(DocObject o) =>
        new(o.Id, o.TypeKey, o.Title, o.OwnerPersonId, o.ResponsiblePersonId, o.ExpiresOn, o.UpdatedAt, o.Fields, o.LifecycleState, o.LifecycleDate,
            o.LastReviewedOn, o.OverdueSince);

    private static async Task<ObjectDto> ToDtoAsync(DocObject item, Caller caller, ObjectAccess access, MyDocsDbContext db, CancellationToken ct)
    {
        var data = new ObjectData { Title = item.Title, Fields = new(item.Fields), Locations = new(item.Locations), Notes = item.Notes, ExpiresOn = item.ExpiresOn };
        var canManage = ObjectAccess.CanManage(caller, item);
        var shares = item.OwnerPersonId == caller.PersonId && item.OwnerPersonId is not null
            ? await db.ObjectShares.Where(s => s.ObjectId == item.Id).Select(s => new ObjectShareDto(s.UserId, s.CanEdit)).ToListAsync(ct)
            : [];
        return new ObjectDto(item.Id, item.TypeKey, data, item.OwnerPersonId, item.ResponsiblePersonId, shares,
            !item.IsDeleted && await access.CanEditAsync(caller, item, ct), canManage, item.Version, item.CreatedAt, item.UpdatedAt, item.DeletedAt,
            item.LifecycleState, item.LifecycleDate, item.ReplacedById)
        {
            Review = ReviewPlan.Info(item, access.Today),
        };
    }

    private static IResult NotFound() => ApiProblems.Of(ObjectErrors.NotFound, StatusCodes.Status404NotFound);

    private static IResult Forbidden() => ApiProblems.Of(ObjectErrors.Forbidden, StatusCodes.Status403Forbidden);
}
