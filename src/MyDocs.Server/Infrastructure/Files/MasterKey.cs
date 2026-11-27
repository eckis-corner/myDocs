using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace MyDocs.Server.Infrastructure.Files;

/// <summary>
/// Server-Hauptschlüssel (22, 46): 32 Byte Zufall in <c>data/keys/master.key</c>, nicht in der Datenbank. Er verpackt
/// die Schlüssel der Dateien und liefert den Schlüssel für die Dateinamen. Ohne ihn sind alle Dokumente verloren:
/// Er gehört in die Sicherung und auf Papier (M1.7).
/// </summary>
public sealed class MasterKey
{
    private const int Length = 32;
    private readonly byte[] _wrapKey;

    public MasterKey(IOptions<MyDocsOptions> options)
    {
        var directory = Path.Combine(Path.GetFullPath(options.Value.DataDirectory), "keys");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "master.key");
        if (!File.Exists(path))
        {
            var created = RandomNumberGenerator.GetBytes(Length);
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(created);
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }

        var key = File.ReadAllBytes(path);
        if (key.Length != Length)
        {
            throw new InvalidOperationException($"{path} hat nicht {Length} Byte.");
        }

        _wrapKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, key, Length, info: Encoding.ASCII.GetBytes("mydocs/wrap"));
        NameKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, key, Length, info: Encoding.ASCII.GetBytes("mydocs/blob-name"));
        CryptographicOperations.ZeroMemory(key);
    }

    /// <summary>Schlüssel für die Dateinamen (HMAC des Inhalts).</summary>
    internal byte[] NameKey { get; }

    /// <summary>Verpackt einen Dateischlüssel: Nonce (12) | Chiffrat (32) | Tag (16).</summary>
    public byte[] Wrap(ReadOnlySpan<byte> fileKey)
    {
        var output = new byte[12 + fileKey.Length + 16];
        RandomNumberGenerator.Fill(output.AsSpan(0, 12));
        using var aes = new AesGcm(_wrapKey, 16);
        aes.Encrypt(output.AsSpan(0, 12), fileKey, output.AsSpan(12, fileKey.Length), output.AsSpan(12 + fileKey.Length), "mydocs/file-key"u8);
        return output;
    }

    public byte[] Unwrap(ReadOnlySpan<byte> wrapped)
    {
        var key = new byte[wrapped.Length - 28];
        using var aes = new AesGcm(_wrapKey, 16);
        aes.Decrypt(wrapped[..12], wrapped.Slice(12, key.Length), wrapped[^16..], key, "mydocs/file-key"u8);
        return key;
    }
}
