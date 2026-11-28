using System.Globalization;
using System.Text;

namespace MyDocs.Server.Infrastructure.Inbox;

/// <summary>Ein Foto ist kein JPEG, das sich in ein PDF einbetten lässt.</summary>
public sealed class InvalidJpegException() : Exception("Kein unterstütztes JPEG.");

/// <summary>
/// Baut aus JPEG-Seiten ein PDF (Kamera, 23), ohne die Bilder neu zu kodieren: Jede Seite bettet das JPEG unverändert
/// ein (DCTDecode) und ist so groß, dass das Bild in A4 passt. Damit rechnet OCRmyPDF mit der richtigen Auflösung.
/// </summary>
public static class JpegPdf
{
    private const double A4Width = 595.28;
    private const double A4Height = 841.89;

    public sealed record JpegInfo(int Width, int Height, int Components);

    /// <summary>Maße und Farbkanäle aus dem SOF-Segment; nur Graustufen und RGB (der Browser liefert RGB).</summary>
    public static JpegInfo Read(ReadOnlySpan<byte> jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            throw new InvalidJpegException();
        }

        var position = 2;
        while (position + 4 <= jpeg.Length)
        {
            if (jpeg[position] != 0xFF)
            {
                throw new InvalidJpegException();
            }

            var marker = jpeg[position + 1];
            if (marker == 0xFF)
            {
                position++;
                continue;
            }

            var length = (jpeg[position + 2] << 8) | jpeg[position + 3];
            // SOF0–SOF15 außer DHT (C4), JPG (C8) und DAC (CC).
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                if (position + 10 > jpeg.Length)
                {
                    throw new InvalidJpegException();
                }

                var height = (jpeg[position + 5] << 8) | jpeg[position + 6];
                var width = (jpeg[position + 7] << 8) | jpeg[position + 8];
                var components = jpeg[position + 9];
                if (width == 0 || height == 0 || components is not (1 or 3))
                {
                    throw new InvalidJpegException();
                }

                return new JpegInfo(width, height, components);
            }

            if (marker == 0xDA || length < 2)
            {
                break;
            }

            position += 2 + length;
        }

        throw new InvalidJpegException();
    }

    public static byte[] Build(IReadOnlyList<byte[]> pages)
    {
        if (pages.Count == 0)
        {
            throw new InvalidJpegException();
        }

        var infos = pages.Select(p => Read(p)).ToList();
        using var output = new MemoryStream();
        var offsets = new List<long>();

        void Write(string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            output.Write(bytes);
        }

        void Begin(int number)
        {
            while (offsets.Count < number)
            {
                offsets.Add(0);
            }

            offsets[number - 1] = output.Position;
            Write($"{number} 0 obj\n");
        }

        // Objekte: 1 Katalog, 2 Seitenbaum, je Seite 3 Objekte (Seite, Inhalt, Bild).
        Write("%PDF-1.4\n");
        // Kommentar mit Bytes über 127: kennzeichnet die Datei als binär.
        output.Write([0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A]);
        Begin(1);
        Write("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        Begin(2);
        var kids = string.Join(' ', Enumerable.Range(0, pages.Count).Select(i => $"{3 + (i * 3)} 0 R"));
        Write($"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>\nendobj\n");

        for (var i = 0; i < pages.Count; i++)
        {
            var info = infos[i];
            var scale = Math.Min(A4Width / info.Width, A4Height / info.Height);
            var width = Number(info.Width * scale);
            var height = Number(info.Height * scale);
            var page = 3 + (i * 3);

            Begin(page);
            Write($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Resources << /XObject << /Im0 {page + 2} 0 R >> >> /Contents {page + 1} 0 R >>\nendobj\n");

            var content = $"q {width} 0 0 {height} 0 0 cm /Im0 Do Q\n";
            Begin(page + 1);
            Write($"<< /Length {content.Length} >>\nstream\n{content}endstream\nendobj\n");

            Begin(page + 2);
            var colors = info.Components == 1 ? "/DeviceGray" : "/DeviceRGB";
            Write($"<< /Type /XObject /Subtype /Image /Width {info.Width} /Height {info.Height} /ColorSpace {colors} /BitsPerComponent 8 /Filter /DCTDecode /Length {pages[i].Length} >>\nstream\n");
            output.Write(pages[i]);
            Write("\nendstream\nendobj\n");
        }

        var xref = output.Position;
        Write($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            Write($"{offset.ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");
        }

        Write($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");
        return output.ToArray();
    }

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
