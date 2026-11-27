using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyDocs.Server.Data;

namespace MyDocs.Server.Infrastructure.Files;

/// <summary>
/// Verschlüsselte Dateien im Volume (22): <c>data/files/ab/abcdef…</c>, benannt nach dem HMAC-SHA-256 des Inhalts.
/// Gleicher Inhalt liegt nur einmal da; wer die Platte sieht, kann trotzdem nicht prüfen, ob eine bekannte Datei
/// dabei ist. Klartext liegt nie auf der Platte, auch nicht vorübergehend.
/// </summary>
public sealed class BlobStore(MyDocsDbContext db, MasterKey master, IOptions<MyDocsOptions> options, TimeProvider time)
{
    private string Root => Path.Combine(Path.GetFullPath(options.Value.DataDirectory), "files");

    /// <summary>Liest den Klartext, verschlüsselt ihn und liefert Name und Größe. Größer als <paramref name="maxBytes"/>: Ausnahme.</summary>
    public async Task<(string Hash, long Size)> StoreAsync(Stream plain, long maxBytes, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.Combine(Root, "tmp"));
        var temporary = Path.Combine(Root, "tmp", Guid.NewGuid().ToString("N"));
        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, master.NameKey);
            var counting = new CountingStream(plain, hmac, maxBytes);
            await using (var cipher = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await BlobCrypto.EncryptAsync(counting, cipher, key, ct);
            }

            var hash = Convert.ToHexStringLower(hmac.GetHashAndReset());
            var existing = await db.Blobs.FirstOrDefaultAsync(b => b.Hash == hash, ct);
            if (existing is not null)
            {
                existing.RefCount++;
                await db.SaveChangesAsync(ct);
                return (hash, existing.Size);
            }

            var target = PathOf(hash);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(temporary, target, overwrite: true);
            db.Blobs.Add(new Blob { Hash = hash, WrappedKey = master.Wrap(key), Size = counting.Length, RefCount = 1, CreatedAt = time.GetUtcNow() });
            await db.SaveChangesAsync(ct);
            return (hash, counting.Length);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    /// <summary>Entschlüsselt die Datei direkt in den Ausgabestrom (etwa die HTTP-Antwort).</summary>
    public async Task CopyToAsync(string hash, Stream output, CancellationToken ct)
    {
        var blob = await db.Blobs.AsNoTracking().FirstAsync(b => b.Hash == hash, ct);
        var key = master.Unwrap(blob.WrappedKey);
        try
        {
            await using var cipher = new FileStream(PathOf(hash), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            await BlobCrypto.DecryptAsync(cipher, output, key, ct);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    internal string PathOf(string hash) => Path.Combine(Root, hash[..2], hash);

    /// <summary>Entfernt die verschlüsselte Datei, nachdem ihr Eintrag gelöscht ist.</summary>
    public void Delete(string hash)
    {
        var path = PathOf(hash);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>Zählt mit, rechnet den HMAC des Klartexts und bricht bei Überschreiten der Grenze ab.</summary>
    private sealed class CountingStream(Stream inner, IncrementalHash hmac, long maxBytes) : Stream
    {
        private long _length;

        public override long Length => _length;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Position { get => _length; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Count(buffer.AsSpan(offset, inner.Read(buffer, offset, count)));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            return Count(buffer.Span[..read]);
        }

        private int Count(ReadOnlySpan<byte> data)
        {
            _length += data.Length;
            if (_length > maxBytes)
            {
                throw new FileTooLargeException();
            }

            hmac.AppendData(data);
            return data.Length;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>Die Datei ist größer als erlaubt (78).</summary>
public sealed class FileTooLargeException : Exception
{
    public FileTooLargeException()
        : base("Datei zu groß.")
    {
    }
}
