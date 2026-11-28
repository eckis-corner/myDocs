using System.Linq.Expressions;
using MyDocs.Server.Data;
using MyDocs.Server.Infrastructure.Objects;

namespace MyDocs.Server.Infrastructure.Inbox;

/// <summary>
/// Rechte im Posteingang (20, 142): Den privaten Posteingang sieht und bearbeitet nur seine Person, den des Haushalts
/// jedes Mitglied. Gäste haben keinen Posteingang. Die Admin-Einsicht gilt hier nicht, weil noch nichts zugeordnet ist.
/// </summary>
public static class InboxAccess
{
    public static Expression<Func<Document, bool>> VisibleTo(Caller caller)
    {
        var person = caller.PersonId;
        var member = caller.IsMember;
        return d => d.InInbox && member && (d.InboxPersonId == null || (person != null && d.InboxPersonId == person));
    }

    public static bool CanSee(Caller caller, Document document) =>
        document.InInbox && caller.IsMember && (document.InboxPersonId == null || (caller.PersonId is { } person && document.InboxPersonId == person));
}
