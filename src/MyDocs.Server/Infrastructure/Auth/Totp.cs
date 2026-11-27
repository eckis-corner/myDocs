using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace MyDocs.Server.Infrastructure.Auth;

/// <summary>TOTP nach RFC 6238: HMAC-SHA-1, 6 Ziffern, 30 Sekunden – das, was alle Authenticator-Apps können (wie myLife).</summary>
internal static class Totp
{
    public const int Digits = 6;
    public const int PeriodSeconds = 30;

    public static long StepAt(DateTimeOffset time) => time.ToUnixTimeSeconds() / PeriodSeconds;

    public static string Compute(byte[] secret, long step)
    {
        var counter = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
#pragma warning disable CA5350 // RFC 6238 und alle Authenticator-Apps verwenden HMAC-SHA-1; als HMAC ist SHA-1 weiterhin sicher.
        var hash = HMACSHA1.HashData(secret, counter);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset)) & 0x7FFFFFFF;
        return (binary % 1_000_000).ToString(CultureInfo.InvariantCulture).PadLeft(Digits, '0');
    }

    /// <summary>Prüft einen Code im Fenster ±1 Zeitschritt (Uhrabweichung). Liefert den passenden Schritt oder null.</summary>
    public static long? Verify(byte[] secret, string? code, DateTimeOffset now)
    {
        var normalized = (code ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal);
        if (normalized.Length != Digits || !normalized.All(char.IsAsciiDigit))
        {
            return null;
        }

        var current = StepAt(now);
        for (var step = current - 1; step <= current + 1; step++)
        {
            if (CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(Compute(secret, step)), System.Text.Encoding.ASCII.GetBytes(normalized)))
            {
                return step;
            }
        }

        return null;
    }
}

/// <summary>Base32 nach RFC 4648 ohne Auffüllung, wie Identity den Schlüssel der Authenticator-App speichert.</summary>
internal static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static byte[] Decode(string text)
    {
        var clean = text.Trim().TrimEnd('=').Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        var output = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            var value = Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (value < 0)
            {
                throw new FormatException("Kein Base32.");
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output.Add((byte)((buffer >> bits) & 0xFF));
            }
        }

        return [.. output];
    }
}
