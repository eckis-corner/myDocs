using System.Security.Cryptography;
using System.Text;

namespace MyDocs.Desktop.Core.Auth;

/// <summary>PKCE mit S256 (RFC 7636): 256 Bit Zufall als Verifier, dessen SHA-256 als Challenge.</summary>
public sealed record Pkce(string Verifier, string Challenge)
{
    public static Pkce Create()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        return new Pkce(verifier, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    public static string NewState() => Base64Url(RandomNumberGenerator.GetBytes(16));

    internal static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
