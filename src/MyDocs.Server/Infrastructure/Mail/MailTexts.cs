using System.Globalization;
using System.Net;
using System.Resources;
using System.Text;
using MyDocs.UI.Resources;

namespace MyDocs.Server.Infrastructure.Mail;

/// <summary>Schlüssel der Mailvorlagen; Texte unter <c>Mail_{Vorlage}_subject</c> und <c>_body</c> in den Texten der Oberfläche.</summary>
public static class MailTemplate
{
    public const string Test = "test";
    public const string Activation = "activation";
    public const string Reset = "reset";
    public const string Digest = "digest";
    public const string Urgent = "urgent";

    public static IReadOnlyList<string> All { get; } = [Test, Activation, Reset, Digest, Urgent];
}

/// <summary>Fertig gesetzte Mail: Betreff, Text- und HTML-Fassung.</summary>
public sealed record RenderedMail(string Subject, string Text, string Html);

/// <summary>
/// Setzt Mails in der Sprache des Empfängers aus denselben Texten wie die Oberfläche. Platzhalter in geschweiften
/// Klammern, etwa <c>{name}</c>; der Parameter <c>link</c> wird in der HTML-Fassung zum Link. Leere Absätze entfallen.
/// </summary>
public static class MailTexts
{
    private static readonly ResourceManager Resources = new(typeof(SharedResource));

    public static CultureInfo Culture(string? language) => CultureInfo.GetCultureInfo(language is "en" ? "en" : "de");

    /// <summary>Ein Text der Oberfläche in der Sprache der Mail, etwa der Titel eines automatischen Vorgangs.</summary>
    public static string? Phrase(string key, string? language) => Resources.GetString(key, Culture(language));

    public static RenderedMail Render(string template, string language, IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var culture = Culture(language);
        var subject = Fill(Text($"Mail_{template}_subject", culture), parameters);
        var body = Fill(Text($"Mail_{template}_body", culture), parameters);
        var footer = Text("Mail_footer", culture);
        var paragraphs = body.ReplaceLineEndings("\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Append(footer).ToList();

        var html = new StringBuilder("<!DOCTYPE html><html><body style=\"font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:1.5;color:#202d39\">");
        foreach (var paragraph in paragraphs)
        {
            var encoded = WebUtility.HtmlEncode(paragraph).ReplaceLineEndings("<br>");
            if (parameters.TryGetValue("link", out var link) && link.Length > 0)
            {
                var encodedLink = WebUtility.HtmlEncode(link);
                encoded = encoded.Replace(encodedLink, $"<a href=\"{encodedLink}\">{encodedLink}</a>", StringComparison.Ordinal);
            }

            var style = paragraph == footer ? " style=\"color:#6b7785;font-size:12px\"" : string.Empty;
            html.Append(CultureInfo.InvariantCulture, $"<p{style}>{encoded}</p>");
        }

        html.Append("</body></html>");
        return new RenderedMail(subject, string.Join("\n\n", paragraphs) + "\n", html.ToString());
    }

    private static string Text(string key, CultureInfo culture) =>
        Resources.GetString(key, culture) ?? throw new InvalidOperationException($"Mailtext fehlt: {key}");

    private static string Fill(string text, IReadOnlyDictionary<string, string> parameters)
    {
        foreach (var (name, value) in parameters)
        {
            text = text.Replace("{" + name + "}", value, StringComparison.Ordinal);
        }

        return text;
    }
}
