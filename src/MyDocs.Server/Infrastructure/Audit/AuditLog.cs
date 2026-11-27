using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using MyDocs.Server.Data;

namespace MyDocs.Server.Infrastructure.Audit;

/// <summary>Schreibt das Audit-Log (35). Der Eintrag wird mit dem nächsten <c>SaveChanges</c> gespeichert, außer bei <see cref="WriteAsync"/>.</summary>
public sealed class AuditLog(MyDocsDbContext db, IHttpContextAccessor http, TimeProvider time)
{
    public void Add(string action, Guid? actor = null, Guid? owner = null, Guid? objectId = null, Guid? documentId = null, string? title = null, string? details = null)
    {
        var context = http.HttpContext;
        db.AuditLog.Add(new AuditEntry
        {
            At = time.GetUtcNow(),
            ActorId = actor ?? CurrentUser(context?.User),
            OwnerPersonId = owner,
            Action = action,
            ObjectId = objectId,
            DocumentId = documentId,
            Title = title?.Length > 200 ? title[..200] : title,
            Ip = Truncate(context?.Connection.RemoteIpAddress),
            Details = details?.Length > 500 ? details[..500] : details,
        });
    }

    public async Task WriteAsync(string action, Guid? actor = null, Guid? owner = null, Guid? objectId = null, Guid? documentId = null, string? title = null, string? details = null, CancellationToken ct = default)
    {
        Add(action, actor, owner, objectId, documentId, title, details);
        await db.SaveChangesAsync(ct);
    }

    private static Guid? CurrentUser(ClaimsPrincipal? user) =>
        Guid.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    /// <summary>IPv4 ohne das letzte Byte, IPv6 auf /48 gekürzt.</summary>
    internal static string? Truncate(IPAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            bytes[3] = 0;
        }
        else
        {
            Array.Clear(bytes, 6, bytes.Length - 6);
        }

        return new IPAddress(bytes).ToString();
    }
}
