using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MyDocs.Server.Data;

namespace MyDocs.Server.Infrastructure.Auth;

/// <summary>
/// Einmal-Tokens mit 256 Bit Zufall, gespeichert nur als SHA-256. Ein neues Token gleichen Zwecks ersetzt die alten
/// desselben Kontos.
/// </summary>
public sealed class OneTimeTokens(MyDocsDbContext db, TimeProvider time)
{
    public static readonly TimeSpan ActivationLifetime = TimeSpan.FromDays(7);

    public async Task<(string Token, DateTimeOffset ExpiresAt)> IssueAsync(Guid userId, string purpose, TimeSpan lifetime, CancellationToken ct)
    {
        await db.OneTimeTokens.Where(t => t.UserId == userId && t.Purpose == purpose && t.UsedAt == null).ExecuteDeleteAsync(ct);
        var token = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var now = time.GetUtcNow();
        var entry = new OneTimeToken
        {
            Id = Guid.CreateVersion7(),
            Purpose = purpose,
            UserId = userId,
            TokenHash = Hash(token),
            CreatedAt = now,
            ExpiresAt = now + lifetime,
        };
        db.OneTimeTokens.Add(entry);
        await db.SaveChangesAsync(ct);
        return (token, entry.ExpiresAt);
    }

    /// <summary>Gültiges, unbenutztes Token oder null. Verbraucht es nicht.</summary>
    public async Task<OneTimeToken?> FindAsync(string? token, string purpose, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100)
        {
            return null;
        }

        var hash = Hash(token.Trim());
        var now = time.GetUtcNow();
        return await db.OneTimeTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == purpose && t.UsedAt == null && t.ExpiresAt > now, ct);
    }

    public async Task ConsumeAsync(OneTimeToken entry, CancellationToken ct)
    {
        entry.UsedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }

    private static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
