namespace MyDocs.Server.Infrastructure.Processing;

/// <summary>Was der Worker mit einer Datei anfangen kann.</summary>
public static class FileKind
{
    public const string Pdf = "pdf";
    public const string Image = "image";
    public const string Office = "office";
    public const string Text = "text";
    public const string Other = "other";
}

/// <summary>
/// Erkennt die Art einer Datei an ihren ersten Bytes (24), nicht am Typ, den der Browser meldet. Office-Dateien sind
/// Zip- oder OLE-Container wie viele andere; dort entscheidet zusätzlich die Endung.
/// </summary>
public static class FileKinds
{
    public const int HeaderLength = 4096;

    private static readonly HashSet<string> ZipOffice = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".docm", ".dotx", ".xlsx", ".xlsm", ".pptx", ".pptm", ".odt", ".ods", ".odp",
    };

    private static readonly HashSet<string> OleOffice = new(StringComparer.OrdinalIgnoreCase) { ".doc", ".dot", ".xls", ".ppt" };

    private static readonly HashSet<string> PlainText = new(StringComparer.OrdinalIgnoreCase) { ".txt", ".csv", ".md" };

    public static string Detect(ReadOnlySpan<byte> header, string fileName)
    {
        var extension = Path.GetExtension(fileName);
        // %PDF- darf laut Norm innerhalb der ersten 1024 Bytes stehen.
        if (header[..Math.Min(header.Length, 1024)].IndexOf("%PDF-"u8) >= 0)
        {
            return FileKind.Pdf;
        }

        if (header.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }) || header.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })
            || header.StartsWith("II*\0"u8) || header.StartsWith("MM\0*"u8))
        {
            return FileKind.Image;
        }

        if ((header.StartsWith("PK\u0003\u0004"u8) && ZipOffice.Contains(extension))
            || (header.StartsWith(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }) && OleOffice.Contains(extension))
            || header.StartsWith("{\\rtf"u8))
        {
            return FileKind.Office;
        }

        if (PlainText.Contains(extension) && header.IndexOf((byte)0) < 0)
        {
            return FileKind.Text;
        }

        return FileKind.Other;
    }

    /// <summary>Endung für den Worker; LibreOffice wählt danach den Filter.</summary>
    public static string ExtensionFor(string kind, string fileName, ReadOnlySpan<byte> header) => kind switch
    {
        FileKind.Office when header.StartsWith("{\\rtf"u8) => ".rtf",
        FileKind.Office or FileKind.Text => Path.GetExtension(fileName).ToLowerInvariant(),
        FileKind.Pdf => ".pdf",
        _ => ".bin",
    };
}
