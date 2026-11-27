using System.Text;
using QRCoder;

namespace MyDocs.UI.Services;

/// <summary>QR-Codes in der Oberfläche als SVG-Daten-URI (erlaubt durch <c>img-src data:</c>). Inhalte gehen an keinen Dienst.</summary>
public static class QrCodes
{
    public static string ToDataUri(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var svg = new SvgQRCode(data).GetGraphic(8);
        return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
    }
}
