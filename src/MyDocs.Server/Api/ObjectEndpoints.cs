using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MyCommon.Catalog;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Objects;
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
        return app;
    }

    private static async Task<IResult> ListAsync(int? area, string? type, ClaimsPrincipal principal, ObjectAccess access, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        var query = access.Visible(caller);
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
        return item is null ? NotFound() : TypedResults.Ok(await ToDtoAsync(item, caller, access, db, ct));
    }

    private static async Task<IResult> CreateAsync(ObjectSaveRequest request, ClaimsPrincipal principal, ObjectAccess access,
        MyDocsDbContext db, TimeProvider time, CancellationToken ct)
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
        };
        Apply(item, request.Data);
        db.Objects.Add(item);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ToDtoAsync(item, caller, access, db, ct));
    }

    private static async Task<IResult> UpdateAsync(Guid id, ObjectSaveRequest request, ClaimsPrincipal principal, ObjectAccess access,
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
        var before = Snapshot(item);
        item.OwnerPersonId = request.OwnerPersonId;
        item.ResponsiblePersonId = request.ResponsiblePersonId;
        Apply(item, request.Data);
        item.UpdatedAt = time.GetUtcNow();
        AddHistory(db, item, before, Snapshot(item), caller.UserId, item.UpdatedAt);
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

    private static async Task<IResult> DeleteAsync(Guid id, ClaimsPrincipal principal, ObjectAccess access, MyDocsDbContext db, TimeProvider time, CancellationToken ct)
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
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> RestoreAsync(Guid id, ClaimsPrincipal principal, ObjectAccess access, MyDocsDbContext db, TimeProvider time, CancellationToken ct)
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
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ToDtoAsync(item, caller, access, db, ct));
    }

    private static async Task<IResult> SharesAsync(Guid id, List<ObjectShareDto> shares, ClaimsPrincipal principal, ObjectAccess access,
        MyDocsDbContext db, CancellationToken ct)
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
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await ToDtoAsync(item, caller, access, db, ct));
    }

    private static async Task<IResult> HistoryAsync(Guid id, ClaimsPrincipal principal, ObjectAccess access, MyDocsDbContext db, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (!await access.Visible(caller).AnyAsync(o => o.Id == id, ct) && !await access.Visible(caller, deleted: true).AnyAsync(o => o.Id == id, ct))
        {
            return NotFound();
        }

        var entries = await (from h in db.ObjectHistory
                             join u in db.Users on h.ActorId equals u.Id into actors
                             from actor in actors.DefaultIfEmpty()
                             where h.ObjectId == id
                             orderby h.At descending, h.Id descending
                             select new ObjectHistoryDto(h.At, actor != null ? actor.DisplayName : "?", h.Field, h.OldValue, h.NewValue)).Take(500).ToListAsync(ct);
        return TypedResults.Ok(entries);
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

    private static void AddHistory(MyDocsDbContext db, DocObject item, Dictionary<string, string?> before, Dictionary<string, string?> after, Guid actor, DateTimeOffset at)
    {
        foreach (var key in before.Keys.Union(after.Keys).Order(StringComparer.Ordinal))
        {
            var old = before.GetValueOrDefault(key);
            var now = after.GetValueOrDefault(key);
            if (old != now)
            {
                db.ObjectHistory.Add(new ObjectHistoryEntry { ObjectId = item.Id, At = at, ActorId = actor, Field = key, OldValue = old, NewValue = now });
            }
        }
    }

    private static ObjectSummaryDto Summary(DocObject o) =>
        new(o.Id, o.TypeKey, o.Title, o.OwnerPersonId, o.ResponsiblePersonId, o.ExpiresOn, o.UpdatedAt, o.Fields);

    private static async Task<ObjectDto> ToDtoAsync(DocObject item, Caller caller, ObjectAccess access, MyDocsDbContext db, CancellationToken ct)
    {
        var data = new ObjectData { Title = item.Title, Fields = new(item.Fields), Locations = new(item.Locations), Notes = item.Notes, ExpiresOn = item.ExpiresOn };
        var canManage = ObjectAccess.CanManage(caller, item);
        var shares = item.OwnerPersonId == caller.PersonId && item.OwnerPersonId is not null
            ? await db.ObjectShares.Where(s => s.ObjectId == item.Id).Select(s => new ObjectShareDto(s.UserId, s.CanEdit)).ToListAsync(ct)
            : [];
        return new ObjectDto(item.Id, item.TypeKey, data, item.OwnerPersonId, item.ResponsiblePersonId, shares,
            !item.IsDeleted && await access.CanEditAsync(caller, item, ct), canManage, item.Version, item.CreatedAt, item.UpdatedAt, item.DeletedAt);
    }

    private static IResult NotFound() => ApiProblems.Of(ObjectErrors.NotFound, StatusCodes.Status404NotFound);

    private static IResult Forbidden() => ApiProblems.Of(ObjectErrors.Forbidden, StatusCodes.Status403Forbidden);
}
