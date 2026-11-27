using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Audit;
using MyDocs.Server.Infrastructure.Objects;
using MyDocs.Server.Infrastructure.Tasks;
using MyDocs.Shared.Audit;
using MyDocs.Shared.Tasks;

namespace MyDocs.Server.Api;

/// <summary>Vorgänge (11) und Kommentare (50): anlegen, ändern, Status, Abschluss mit Ergebnis.</summary>
internal static class TaskEndpoints
{
    public static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tasks").RequireAuthorization();
        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("/", CreateAsync);
        group.MapPut("/{id:guid}", UpdateAsync);
        group.MapPost("/{id:guid}/state", StateAsync);
        group.MapPost("/{id:guid}/postpone", PostponeAsync);
        group.MapGet("/{id:guid}/comments", (Guid id, ClaimsPrincipal principal, ObjectAccess access, TaskAccess tasks, MyDocsDbContext db, CancellationToken ct) =>
            TaskCommentsAsync(id, principal, access, tasks, db, ct));
        group.MapPost("/{id:guid}/comments", AddTaskCommentAsync);

        var objects = app.MapGroup("/api/objects/{id:guid}").RequireAuthorization();
        objects.MapGet("/tasks", ObjectTasksAsync);
        objects.MapGet("/comments", ObjectCommentsAsync);
        objects.MapPost("/comments", AddObjectCommentAsync);

        app.MapDelete("/api/comments/{id:guid}", DeleteCommentAsync).RequireAuthorization();
        return app;
    }

    /// <summary>Vorgänge: <paramref name="scope"/> „mine“ (zuständig bin ich) oder alle; <paramref name="open"/> nur offene.</summary>
    private static async Task<IResult> ListAsync(string? scope, bool? open, ClaimsPrincipal principal, ObjectAccess access, TaskAccess tasks, MyDocsDbContext db, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        var query = tasks.Visible(caller);
        if (open != false)
        {
            query = query.Where(t => t.State != TaskStates.Done && t.State != TaskStates.Discarded);
        }

        if (scope == "mine")
        {
            query = query.Where(t => t.ResponsiblePersonId == caller.PersonId);
        }

        var items = await query.OrderBy(t => t.DueOn ?? DateOnly.MaxValue).ThenBy(t => t.CreatedAt).Take(1000).ToListAsync(ct);
        return TypedResults.Ok(await ToDtosAsync(items, caller, tasks, db, ct));
    }

    private static async Task<IResult> ObjectTasksAsync(Guid id, bool? open, ClaimsPrincipal principal, ObjectAccess access, TaskAccess tasks, MyDocsDbContext db, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (!await access.Visible(caller).AnyAsync(o => o.Id == id, ct))
        {
            return NotFound();
        }

        var query = tasks.Visible(caller).Where(t => db.TaskObjects.Any(l => l.TaskId == t.Id && l.ObjectId == id));
        if (open == true)
        {
            query = query.Where(t => t.State != TaskStates.Done && t.State != TaskStates.Discarded);
        }

        var items = await query.OrderBy(t => t.State == TaskStates.Done || t.State == TaskStates.Discarded).ThenBy(t => t.DueOn ?? DateOnly.MaxValue).ToListAsync(ct);
        return TypedResults.Ok(await ToDtosAsync(items, caller, tasks, db, ct));
    }

    private static async Task<IResult> GetAsync(Guid id, ClaimsPrincipal principal, ObjectAccess access, TaskAccess tasks, MyDocsDbContext db, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        return await tasks.Visible(caller).FirstOrDefaultAsync(t => t.Id == id, ct) is { } task
            ? TypedResults.Ok((await ToDtosAsync([task], caller, tasks, db, ct))[0])
            : NotFound();
    }

    private static async Task<IResult> CreateAsync(TaskSaveRequest request, ClaimsPrincipal principal, ObjectAccess access, TaskAccess tasks,
        MyDocsDbContext db, AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (await ValidateAsync(request, caller, access, db, ct) is { } problem)
        {
            return problem;
        }

        var objectIds = request.ObjectIds.Distinct().ToList();
        if (objectIds.Count == 0 ? !caller.IsMember : !await CanEditAnyAsync(objectIds, caller, access, ct))
        {
            return Forbidden();
        }

        var task = new WorkTask
        {
            Id = Guid.CreateVersion7(),
            Kind = TaskKinds.Task,
            CreatedAt = time.GetUtcNow(),
            CreatedBy = caller.UserId,
        };
        Apply(task, request);
        db.Tasks.Add(task);
        db.TaskObjects.AddRange(objectIds.Select(o => new TaskObject { TaskId = task.Id, ObjectId = o }));
        audit.Add(AuditActions.TaskCreated, caller.UserId, await OwnerAsync(db, objectIds, ct), objectIds.Count > 0 ? objectIds[0] : null, title: task.Title);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok((await ToDtosAsync([task], caller, tasks, db, ct))[0]);
    }

    private static async Task<IResult> UpdateAsync(Guid id, TaskSaveRequest request, ClaimsPrincipal principal, ObjectAccess access, TaskAccess tasks,
        MyDocsDbContext db, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (await tasks.Visible(caller).FirstOrDefaultAsync(t => t.Id == id, ct) is not { } task)
        {
            return NotFound();
        }

        if (!await tasks.CanEditAsync(caller, task, ct))
        {
            return Forbidden();
        }

        if (await ValidateAsync(request, caller, access, db, ct) is { } problem)
        {
            return problem;
        }

        db.Entry(task).Property(t => t.Version).OriginalValue = request.Version;
        Apply(task, request);
        // Von Hand geänderter Titel: kein übersetzter Text der Regel mehr.
        if (task.TitleKey is not null && request.Title != task.Title)
        {
            task.TitleKey = null;
        }

        var current = await db.TaskObjects.Where(l => l.TaskId == id).ToListAsync(ct);
        var wanted = request.ObjectIds.Distinct().ToHashSet();
        var changed = current.Select(l => l.ObjectId).ToHashSet();
        changed.SymmetricExceptWith(wanted);
        if (changed.Count > 0 && !await CanEditAllAsync(changed, caller, access, ct))
        {
            return Forbidden();
        }

        db.TaskObjects.RemoveRange(current.Where(l => !wanted.Contains(l.ObjectId)));
        db.TaskObjects.AddRange(wanted.Where(w => current.All(l => l.ObjectId != w)).Select(w => new TaskObject { TaskId = id, ObjectId = w }));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiProblems.Of(TaskErrors.Conflict, StatusCodes.Status409Conflict);
        }

        return TypedResults.Ok((await ToDtosAsync([task], caller, tasks, db, ct))[0]);
    }

    /// <summary>
    /// Statuswechsel (11, 15): „wartet auf Dritte“ nur mit Datum; Prüfungen und Fristen nur mit Ergebnis erledigt.
    /// Erledigte und verworfene Vorgänge lassen sich wieder öffnen.
    /// </summary>
    private static async Task<IResult> StateAsync(Guid id, TaskStateRequest request, ClaimsPrincipal principal, ObjectAccess access, TaskAccess tasks,
        MyDocsDbContext db, AuditLog audit, TimeProvider time, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (await tasks.Visible(caller).FirstOrDefaultAsync(t => t.Id == id, ct) is not { } task)
        {
            return NotFound();
        }

        if (!await tasks.CanEditAsync(caller, task, ct))
        {
            return Forbidden();
        }

        if (!TaskStates.All.Contains(request.State))
        {
            return ApiProblems.Of(TaskErrors.StateInvalid);
        }

        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        if (request.State == TaskStates.Waiting && (request.WaitUntil is not { } until || until <= today))
        {
            return ApiProblems.Of(TaskErrors.WaitUntilRequired);
        }

        if (request.State == TaskStates.Done && TaskKinds.NeedOutcome.Contains(task.Kind) && !TaskOutcomes.All.Contains(request.Outcome ?? string.Empty))
        {
            return ApiProblems.Of(TaskErrors.OutcomeRequired);
        }

        db.Entry(task).Property(t => t.Version).OriginalValue = request.Version;
        task.State = request.State;
        task.WaitUntil = request.State == TaskStates.Waiting ? request.WaitUntil : null;
        if (TaskStates.Closed.Contains(request.State))
        {
            task.Outcome = request.State == TaskStates.Done ? request.Outcome : null;
            task.OutcomeNote = string.IsNullOrWhiteSpace(request.OutcomeNote) ? null : request.OutcomeNote.Trim()[..Math.Min(request.OutcomeNote.Trim().Length, 2000)];
            task.ClosedAt = time.GetUtcNow();
            task.ClosedBy = caller.UserId;
            var linked = await db.TaskObjects.Where(l => l.TaskId == id).Select(l => l.ObjectId).ToListAsync(ct);
            if (request.State == TaskStates.Done && TaskKinds.NeedOutcome.Contains(task.Kind))
            {
                // Abschluss mit Ergebnis (15): zuletzt geprüft heute, „überfällig“ entfällt, die nächste Prüfung ergibt sich aus dem Intervall.
                foreach (var item in await db.Objects.Where(o => linked.Contains(o.Id)).ToListAsync(ct))
                {
                    item.LastReviewedOn = today;
                    item.OverdueSince = null;
                    if (task.Kind == TaskKinds.Review)
                    {
                        item.NextReviewOn = null;
                    }

                    db.ObjectHistory.Add(new ObjectHistoryEntry { ObjectId = item.Id, At = time.GetUtcNow(), ActorId = caller.UserId, Field = "reviewed", NewValue = request.Outcome });
                }
            }

            audit.Add(AuditActions.TaskClosed, caller.UserId, await OwnerAsync(db, linked, ct), linked.Count > 0 ? linked[0] : null, title: task.Title,
                details: string.Join(", ", new[] { request.State, task.Outcome }.Where(v => v is not null)));
        }
        else
        {
            task.Outcome = null;
            task.OutcomeNote = null;
            task.ClosedAt = null;
            task.ClosedBy = null;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiProblems.Of(TaskErrors.Conflict, StatusCodes.Status409Conflict);
        }

        return TypedResults.Ok((await ToDtosAsync([task], caller, tasks, db, ct))[0]);
    }

    /// <summary>
    /// Zurückstellen (16): nur mit neuem Datum höchstens 30 Tage voraus und mit Begründung. Die Begründung steht als
    /// Kommentar am Vorgang, die Zahl der Rückstellungen am Vorgang selbst; die Eskalation beginnt neu.
    /// </summary>
    private static async Task<IResult> PostponeAsync(Guid id, TaskPostponeRequest request, ClaimsPrincipal principal, ObjectAccess access, TaskAccess tasks,
        MyDocsDbContext db, TimeProvider time, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (await tasks.Visible(caller).FirstOrDefaultAsync(t => t.Id == id, ct) is not { } task || TaskStates.Closed.Contains(task.State))
        {
            return NotFound();
        }

        if (!await tasks.CanEditAsync(caller, task, ct))
        {
            return Forbidden();
        }

        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        if (request.Until <= today || request.Until > today.AddDays(MyDocs.Shared.Objects.ReviewRules.MaxPostponeDays))
        {
            return ApiProblems.Of(TaskErrors.PostponeInvalid);
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return ApiProblems.Of(TaskErrors.ReasonRequired);
        }

        db.Entry(task).Property(t => t.Version).OriginalValue = request.Version;
        task.DueOn = request.Until;
        task.EscalationLevel = 0;
        task.PostponedCount++;
        db.Comments.Add(new Comment
        {
            Id = Guid.CreateVersion7(),
            TaskId = id,
            AuthorId = caller.UserId,
            At = time.GetUtcNow(),
            Text = $"Zurückgestellt bis {request.Until:dd.MM.yyyy}: {request.Reason.Trim()[..Math.Min(request.Reason.Trim().Length, 1000)]}",
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiProblems.Of(TaskErrors.Conflict, StatusCodes.Status409Conflict);
        }

        return TypedResults.Ok((await ToDtosAsync([task], caller, tasks, db, ct))[0]);
    }

    private static async Task<IResult> TaskCommentsAsync(Guid id, ClaimsPrincipal principal, ObjectAccess access, TaskAccess tasks, MyDocsDbContext db, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        return await tasks.Visible(caller).AnyAsync(t => t.Id == id, ct)
            ? TypedResults.Ok(await CommentsAsync(db.Comments.Where(c => c.TaskId == id), db, ct))
            : NotFound();
    }

    private static async Task<IResult> ObjectCommentsAsync(Guid id, ClaimsPrincipal principal, ObjectAccess access, MyDocsDbContext db, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        return await access.Visible(caller).AnyAsync(o => o.Id == id, ct)
            ? TypedResults.Ok(await CommentsAsync(db.Comments.Where(c => c.ObjectId == id), db, ct))
            : NotFound();
    }

    /// <summary>Kommentieren darf, wer den Vorgang bzw. das Objekt sieht und Mitglied ist oder es bearbeiten darf.</summary>
    private static async Task<IResult> AddTaskCommentAsync(Guid id, CommentRequest request, ClaimsPrincipal principal, ObjectAccess access, TaskAccess tasks,
        MyDocsDbContext db, TimeProvider time, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (await tasks.Visible(caller).FirstOrDefaultAsync(t => t.Id == id, ct) is not { } task)
        {
            return NotFound();
        }

        if (!await tasks.CanEditAsync(caller, task, ct))
        {
            return Forbidden();
        }

        return await AddCommentAsync(new Comment { TaskId = id }, request, caller, db, time, ct);
    }

    private static async Task<IResult> AddObjectCommentAsync(Guid id, CommentRequest request, ClaimsPrincipal principal, ObjectAccess access,
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

        return await AddCommentAsync(new Comment { ObjectId = id }, request, caller, db, time, ct);
    }

    private static async Task<IResult> AddCommentAsync(Comment comment, CommentRequest request, Caller caller, MyDocsDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return ApiProblems.Of(TaskErrors.TitleRequired);
        }

        comment.Id = Guid.CreateVersion7();
        comment.AuthorId = caller.UserId;
        comment.At = time.GetUtcNow();
        comment.Text = request.Text.Trim()[..Math.Min(request.Text.Trim().Length, 4000)];
        db.Comments.Add(comment);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok((await CommentsAsync(db.Comments.Where(c => c.Id == comment.Id), db, ct))[0]);
    }

    /// <summary>Eigene Kommentare lassen sich löschen.</summary>
    private static async Task<IResult> DeleteCommentAsync(Guid id, ClaimsPrincipal principal, ObjectAccess access, MyDocsDbContext db, CancellationToken ct)
    {
        var caller = await access.CallerAsync(principal, ct);
        if (await db.Comments.FirstOrDefaultAsync(c => c.Id == id && c.AuthorId == caller.UserId, ct) is not { } comment)
        {
            return NotFound();
        }

        db.Comments.Remove(comment);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static Task<List<CommentDto>> CommentsAsync(IQueryable<Comment> query, MyDocsDbContext db, CancellationToken ct) =>
        (from c in query
         join u in db.Users on c.AuthorId equals u.Id into authors
         from author in authors.DefaultIfEmpty()
         orderby c.At
         select new CommentDto(c.Id, author != null ? author.DisplayName : "?", c.AuthorId, c.At, c.Text)).ToListAsync(ct);

    private static async Task<IResult?> ValidateAsync(TaskSaveRequest request, Caller caller, ObjectAccess access, MyDocsDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200)
        {
            return ApiProblems.Of(TaskErrors.TitleRequired);
        }

        if (request.ResponsiblePersonId is { } person && !await db.Persons.AnyAsync(p => p.Id == person && p.UserId != null, ct))
        {
            return ApiProblems.Of(TaskErrors.ResponsibleInvalid);
        }

        var ids = request.ObjectIds.Distinct().ToList();
        return await access.Visible(caller).CountAsync(o => ids.Contains(o.Id), ct) == ids.Count ? null : NotFound();
    }

    private static void Apply(WorkTask task, TaskSaveRequest request)
    {
        task.Title = request.Title.Trim();
        task.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim()[..Math.Min(request.Note.Trim().Length, 4000)];
        task.ResponsiblePersonId = request.ResponsiblePersonId;
        task.DueOn = request.DueOn;
        task.Priority = request.Priority == TaskPriorities.High ? TaskPriorities.High : TaskPriorities.Normal;
    }

    private static async Task<bool> CanEditAnyAsync(IReadOnlyCollection<Guid> ids, Caller caller, ObjectAccess access, CancellationToken ct)
    {
        foreach (var item in await access.Visible(caller).Where(o => ids.Contains(o.Id)).ToListAsync(ct))
        {
            if (await access.CanEditAsync(caller, item, ct))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> CanEditAllAsync(HashSet<Guid> ids, Caller caller, ObjectAccess access, CancellationToken ct)
    {
        var items = await access.Visible(caller).Where(o => ids.Contains(o.Id)).ToListAsync(ct);
        if (items.Count != ids.Count)
        {
            return false;
        }

        foreach (var item in items)
        {
            if (!await access.CanEditAsync(caller, item, ct))
            {
                return false;
            }
        }

        return true;
    }

    private static Task<Guid?> OwnerAsync(MyDocsDbContext db, IReadOnlyCollection<Guid> objectIds, CancellationToken ct) =>
        db.Objects.Where(o => objectIds.Contains(o.Id) && o.OwnerPersonId != null).Select(o => o.OwnerPersonId).FirstOrDefaultAsync(ct);

    internal static async Task<List<TaskDto>> ToDtosAsync(List<WorkTask> items, Caller caller, TaskAccess tasks, MyDocsDbContext db, CancellationToken ct)
    {
        var ids = items.Select(t => t.Id).ToList();
        var links = await db.TaskObjects.Where(l => ids.Contains(l.TaskId)).ToListAsync(ct);
        var userIds = items.SelectMany(t => new[] { t.CreatedBy, t.ClosedBy ?? Guid.Empty }).Distinct().ToList();
        var names = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var result = new List<TaskDto>();
        foreach (var t in items)
        {
            result.Add(new TaskDto(t.Id, t.Kind, t.Title, t.Note, t.ResponsiblePersonId, t.DueOn, t.Priority, t.State, t.WaitUntil, t.Outcome, t.OutcomeNote,
                [.. links.Where(l => l.TaskId == t.Id).Select(l => l.ObjectId)], await tasks.CanEditAsync(caller, t, ct), t.CreatedAt,
                names.GetValueOrDefault(t.CreatedBy) ?? "myDocs", t.ClosedAt, t.ClosedBy is { } closer ? names.GetValueOrDefault(closer) : null, t.Version)
            {
                TitleKey = t.TitleKey,
                EscalationLevel = t.EscalationLevel,
                PostponedCount = t.PostponedCount,
            });
        }

        return result;
    }

    private static IResult NotFound() => ApiProblems.Of(TaskErrors.NotFound, StatusCodes.Status404NotFound);

    private static IResult Forbidden() => ApiProblems.Of(TaskErrors.Forbidden, StatusCodes.Status403Forbidden);
}
