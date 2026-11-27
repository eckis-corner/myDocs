using Microsoft.EntityFrameworkCore;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Objects;

namespace MyDocs.Server.Infrastructure.Tasks;

/// <summary>
/// Rechte an Vorgängen: Ein Vorgang ohne Objekt gehört dem Haushalt (alle sehen ihn, Mitglieder ändern ihn). Ein
/// Vorgang mit Objekten ist nur sichtbar, wenn alle seine Objekte sichtbar sind, damit er nichts über fremde private
/// Einträge verrät; ändern darf, wer eines davon bearbeiten darf.
/// </summary>
public sealed class TaskAccess(MyDocsDbContext db, ObjectAccess objects)
{
    public IQueryable<WorkTask> Visible(Caller caller)
    {
        var visible = objects.Visible(caller).Select(o => o.Id);
        return db.Tasks.Where(t => db.TaskObjects.Where(l => l.TaskId == t.Id).All(l => visible.Contains(l.ObjectId)));
    }

    public async Task<bool> CanEditAsync(Caller caller, WorkTask task, CancellationToken ct)
    {
        var linked = await db.TaskObjects.Where(l => l.TaskId == task.Id).Select(l => l.ObjectId).ToListAsync(ct);
        if (linked.Count == 0)
        {
            return caller.IsMember;
        }

        foreach (var item in await objects.Visible(caller).Where(o => linked.Contains(o.Id)).ToListAsync(ct))
        {
            if (await objects.CanEditAsync(caller, item, ct))
            {
                return true;
            }
        }

        return false;
    }
}
