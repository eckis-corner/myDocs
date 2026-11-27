using System.Buffers.Binary;
using System.Security.Cryptography;

namespace MyDocs.Server.Infrastructure.Files;

/// <summary>
/// Verschlüsselung einer Datei at rest (22): AES-256-GCM in Blöcken von 1 MiB wie bei myLife. Jede Datei hat ihren
/// eigenen Schlüssel, deshalb genügt als Nonce die Blocknummer. Der letzte Block ist in der AAD markiert, damit
/// abgeschnittene Dateien auffallen.
/// <code>
/// Datei:  "MDB1" | Block 0 | Block 1 | …
/// Block:  Länge (4, Big Endian) | Chiffrat | Tag (16)
/// Nonce:  0 (4) | Blocknummer (8, Big Endian);  AAD: "mydocs/blob" | letzter Block (1)
/// </code>
/// </summary>
internal static class BlobCrypto
{
    public const int ChunkSize = 1024 * 1024;
    private static readonly byte[] Magic = "MDB1"u8.ToArray();

    public static async Task EncryptAsync(Stream plain, Stream cipher, byte[] key, CancellationToken ct)
    {
        await cipher.WriteAsync(Magic, ct);
        using var aes = new AesGcm(key, 16);
        var current = new byte[ChunkSize];
        var next = new byte[ChunkSize];
        var currentLength = await FillAsync(plain, current, ct);
        long index = 0;
        while (true)
        {
            var nextLength = currentLength == ChunkSize ? await FillAsync(plain, next, ct) : 0;
            var last = nextLength == 0;
            var output = new byte[4 + currentLength + 16];
            BinaryPrimitives.WriteInt32BigEndian(output, currentLength);
            aes.Encrypt(Nonce(index), current.AsSpan(0, currentLength), output.AsSpan(4, currentLength), output.AsSpan(4 + currentLength), Aad(last));
            await cipher.WriteAsync(output, ct);
            if (last)
            {
                return;
            }

            (current, next) = (next, current);
            currentLength = nextLength;
            index++;
        }
    }

    public static async Task DecryptAsync(Stream cipher, Stream plain, byte[] key, CancellationToken ct)
    {
        var magic = new byte[4];
        await cipher.ReadExactlyAsync(magic, ct);
        if (!magic.AsSpan().SequenceEqual(Magic))
        {
            throw new CryptographicException("Unbekanntes Format.");
        }

        using var aes = new AesGcm(key, 16);
        var header = new byte[4];
        long index = 0;
        while (true)
        {
            await cipher.ReadExactlyAsync(header, ct);
            var length = BinaryPrimitives.ReadInt32BigEndian(header);
            if (length is < 0 or > ChunkSize)
            {
                throw new CryptographicException("Ungültiger Block.");
            }

            var block = new byte[length + 16];
            await cipher.ReadExactlyAsync(block, ct);
            var last = cipher.Position >= cipher.Length;
            var output = new byte[length];
            aes.Decrypt(Nonce(index), block.AsSpan(0, length), block.AsSpan(length), output, Aad(last));
            await plain.WriteAsync(output, ct);
            if (last)
            {
                return;
            }

            index++;
        }
    }

    private static byte[] Nonce(long index)
    {
        var nonce = new byte[12];
        BinaryPrimitives.WriteInt64BigEndian(nonce.AsSpan(4), index);
        return nonce;
    }

    private static byte[] Aad(bool last) => [.. "mydocs/blob"u8, last ? (byte)1 : (byte)0];

    private static async Task<int> FillAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), ct);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
