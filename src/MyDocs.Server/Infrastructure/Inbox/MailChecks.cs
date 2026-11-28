using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using MimeKit;
using MimeKit.Cryptography;
using MyDocs.Server.Infrastructure.Processing;

namespace MyDocs.Server.Infrastructure.Inbox;

/// <summary>
/// Absenderprüfung (49, 149): Es zählt der oberste <c>Authentication-Results</c>-Header, den der eigene Mailserver
/// beim Empfang einträgt; ist eine Kennung (authserv-id) eingestellt, nur ein Header mit dieser Kennung. Bestanden ist
/// DMARC, oder DKIM bzw. SPF mit einer Domain, die zum Absender passt.
/// </summary>
public static class SenderCheck
{
    public sealed record Verdict(bool Passed, string Reason);

    public static Verdict Verify(MimeMessage message, string fromAddress, string? trustedAuthServId)
    {
        ArgumentNullException.ThrowIfNull(message);
        var fromDomain = Domain(fromAddress);
        if (fromDomain is null)
        {
            return new Verdict(false, "Absender ohne Domain");
        }

        AuthenticationResults? results = null;
        foreach (var header in message.Headers.Where(h => h.Id == HeaderId.AuthenticationResults))
        {
            if (!AuthenticationResults.TryParse(header.RawValue, out var parsed))
            {
                continue;
            }

            if (trustedAuthServId is null || string.Equals(parsed.AuthenticationServiceIdentifier, trustedAuthServId, StringComparison.OrdinalIgnoreCase))
            {
                results = parsed;
                break;
            }
        }

        if (results is null)
        {
            return new Verdict(false, trustedAuthServId is null ? "keine Authentication-Results" : $"keine Authentication-Results von {trustedAuthServId}");
        }

        foreach (var result in results.Results.Where(r => string.Equals(r.Result, "pass", StringComparison.OrdinalIgnoreCase)))
        {
            var method = result.Method.ToLowerInvariant();
            if (method == "dmarc")
            {
                var from = Property(result, "header", "from");
                if (from is null || Aligned(fromDomain, from))
                {
                    return new Verdict(true, "dmarc=pass");
                }
            }
            else if (method == "dkim" && (Property(result, "header", "d") ?? Domain(Property(result, "header", "i"))) is { } d && Aligned(fromDomain, d))
            {
                return new Verdict(true, "dkim=pass");
            }
            else if (method == "spf" && (Domain(Property(result, "smtp", "mailfrom")) ?? Property(result, "smtp", "helo")) is { } spf && Aligned(fromDomain, spf))
            {
                return new Verdict(true, "spf=pass");
            }
        }

        return new Verdict(false, "SPF/DKIM/DMARC nicht bestanden");
    }

    public static string? Domain(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        var at = address.LastIndexOf('@');
        var domain = (at >= 0 ? address[(at + 1)..] : address).Trim().TrimEnd('>', '.').ToLowerInvariant();
        return domain.Length > 0 && domain.Contains('.', StringComparison.Ordinal) ? domain : null;
    }

    /// <summary>Gleiche Domain oder eine Subdomain der anderen (entspannte Ausrichtung wie bei DMARC).</summary>
    internal static bool Aligned(string fromDomain, string other)
    {
        var domain = other.Trim().TrimEnd('.').ToLowerInvariant();
        return domain == fromDomain || fromDomain.EndsWith("." + domain, StringComparison.Ordinal) || domain.EndsWith("." + fromDomain, StringComparison.Ordinal);
    }

    private static string? Property(AuthenticationMethodResult result, string type, string name) =>
        result.Properties.FirstOrDefault(p => string.Equals(p.PropertyType, type, StringComparison.OrdinalIgnoreCase)
            && string.Equals(p.Property, name, StringComparison.OrdinalIgnoreCase))?.Value;
}

/// <summary>
/// Anhänge aus der Positivliste (49, 150): PDF, Bilder, Office und Text, und nur, wenn der Inhalt zur Endung passt.
/// Bilder in der Signatur (eingebettet mit Content-ID) zählen nicht als Anhang.
/// </summary>
public static class MailAttachments
{
    private static readonly HashSet<string> Pdf = new(StringComparer.OrdinalIgnoreCase) { ".pdf" };

    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".gif", ".webp", ".heic" };

    private static readonly HashSet<string> Office = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".docm", ".xls", ".xlsx", ".xlsm", ".ppt", ".pptx", ".pptm", ".odt", ".ods", ".odp", ".rtf", ".txt", ".csv",
    };

    public static IEnumerable<MimePart> Candidates(MimeMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        foreach (var part in message.BodyParts.OfType<MimePart>())
        {
            var named = !string.IsNullOrWhiteSpace(part.FileName);
            if (part.IsAttachment || (named && string.IsNullOrEmpty(part.ContentId)))
            {
                yield return part;
            }
        }
    }

    /// <summary>Endung aus der Positivliste und passender Inhalt.</summary>
    public static bool Accept(string fileName, ReadOnlySpan<byte> header)
    {
        var extension = Path.GetExtension(fileName);
        var kind = FileKinds.Detect(header, fileName);
        if (Pdf.Contains(extension))
        {
            return kind == FileKind.Pdf;
        }

        if (Images.Contains(extension))
        {
            return kind == FileKind.Image
                || header.StartsWith("GIF8"u8)
                || (header.Length >= 12 && header.StartsWith("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
                || (header.Length >= 12 && header[4..8].SequenceEqual("ftyp"u8));
        }

        return Office.Contains(extension) && kind is FileKind.Office or FileKind.Text;
    }

    public static string ContentType(string fileName, string? declared)
    {
        var type = (declared ?? string.Empty).ToLowerInvariant();
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => type.Length is > 0 and <= 200 && !type.Contains("html", StringComparison.Ordinal) && !type.Contains("svg", StringComparison.Ordinal) ? type : "application/octet-stream",
        };
    }
}

/// <summary>Office-Makros (49): markiert, nicht entfernt. OOXML mit vbaProject.bin, OLE mit VBA-Projekt, ODF mit Basic-Bibliothek.</summary>
public static class MacroCheck
{
    private static readonly byte[] OleVbaName = Encoding.Unicode.GetBytes("_VBA_PROJECT");

    public static bool HasMacros(byte[] content, string fileName)
    {
        ArgumentNullException.ThrowIfNull(content);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is ".docm" or ".xlsm" or ".pptm" or ".dotm" or ".xltm")
        {
            return true;
        }

        if (content.AsSpan().StartsWith("PK\u0003\u0004"u8))
        {
            try
            {
                using var zip = new ZipArchive(new MemoryStream(content, writable: false), ZipArchiveMode.Read);
                return zip.Entries.Any(e => e.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)
                    || (e.FullName.StartsWith("Basic/", StringComparison.Ordinal) && e.FullName.Count(c => c == '/') >= 2 && !e.FullName.EndsWith('/')));
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }

        return content.AsSpan().StartsWith(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }) && content.AsSpan().IndexOf(OleVbaName) >= 0;
    }
}

/// <summary>Mailtext als einfacher Text: HTML ohne Skripte, Stile und Bilder (nichts wird nachgeladen).</summary>
public static partial class MailText
{
    public static string From(MimeMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!string.IsNullOrWhiteSpace(message.TextBody))
        {
            return message.TextBody.Trim();
        }

        return string.IsNullOrWhiteSpace(message.HtmlBody) ? string.Empty : HtmlToText(message.HtmlBody);
    }

    /// <summary>Längere HTML-Texte werden gekürzt; eine Mail ist kein Buch.</summary>
    private const int MaxHtmlLength = 1_000_000;

    public static string HtmlToText(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var text = html.Length > MaxHtmlLength ? html[..MaxHtmlLength] : html;
        try
        {
            text = Blocks().Replace(text, string.Empty);
            text = Breaks().Replace(text, "\n");
        }
        catch (RegexMatchTimeoutException)
        {
            // Bösartig verschachteltes HTML: nur die Tags entfernen.
        }

        text = Tags().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);
        var lines = text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n').Select(l => Spaces().Replace(l, " ").Trim());
        return Blank().Replace(string.Join('\n', lines), "\n\n").Trim();
    }

    [GeneratedRegex(@"<(script|style|head)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, matchTimeoutMilliseconds: 2000)]
    private static partial Regex Blocks();

    [GeneratedRegex(@"<br\s*/?>|</(p|div|li|tr|h[1-6]|table|blockquote)\s*>", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 2000)]
    private static partial Regex Breaks();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"[ \t ]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex Blank();
}
