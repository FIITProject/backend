using System.Globalization;
using System.Text.Json;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf.IO;

namespace Fiit.Api.Services;

/// <summary>
/// Пометка на PDF. Координаты — в пунктах относительно левого верхнего угла страницы (как viewport pdf.js при scale = 1).
/// stroke: Points = [x1, y1, x2, y2, …]; text: X, Y — левый верхний угол первой строки.
/// </summary>
public record Annotation(string Type, int Page, string? Color, double Width, double Opacity, double[]? Points, double X, double Y, string? Text, double Size);

/// <summary>Впечатывает пометки преподавателя в PDF студента (П-9.6). Оригинал не меняется — возвращается новый файл.</summary>
public static class PdfAnnotator
{
    public const int MaxAnnotationsJsonLength = 2 * 1024 * 1024;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    static PdfAnnotator()
    {
        if (GlobalFontSettings.FontResolver is null)
            GlobalFontSettings.FontResolver = new EmbeddedFontResolver();
    }

    /// <summary>null — JSON некорректен.</summary>
    public static List<Annotation>? Parse(string json)
    {
        if (json.Length > MaxAnnotationsJsonLength)
            return null;
        try
        {
            var list = JsonSerializer.Deserialize<List<Annotation>>(json, Json);
            return list?.All(a => a.Page >= 0 && a.Type is "stroke" or "text") == true ? list : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static byte[] Apply(byte[] pdf, IReadOnlyList<Annotation> annotations)
    {
        using var input = new MemoryStream(pdf);
        using var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        foreach (var byPage in annotations.GroupBy(a => a.Page))
        {
            if (byPage.Key >= document.PageCount)
                continue;
            using var g = XGraphics.FromPdfPage(document.Pages[byPage.Key], XGraphicsPdfPageOptions.Append);
            foreach (var a in byPage)
            {
                if (a.Type == "stroke")
                    DrawStroke(g, a);
                else
                    DrawText(g, a);
            }
        }

        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        return output.ToArray();
    }

    private static void DrawStroke(XGraphics g, Annotation a)
    {
        if (a.Points is not { Length: >= 2 } p)
            return;
        var pen = new XPen(Color(a.Color, a.Opacity), Math.Clamp(a.Width, 0.5, 60))
        {
            LineCap = XLineCap.Round,
            LineJoin = XLineJoin.Round,
        };
        if (p.Length < 4)
        {
            g.DrawEllipse(new XSolidBrush(pen.Color), p[0] - pen.Width / 2, p[1] - pen.Width / 2, pen.Width, pen.Width);
            return;
        }
        var points = Enumerable.Range(0, p.Length / 2).Select(i => new XPoint(p[2 * i], p[2 * i + 1])).ToArray();
        g.DrawLines(pen, points);
    }

    private static void DrawText(XGraphics g, Annotation a)
    {
        if (string.IsNullOrWhiteSpace(a.Text))
            return;
        var size = Math.Clamp(a.Size, 4, 96);
        var font = new XFont(EmbeddedFontResolver.Family, size);
        var brush = new XSolidBrush(Color(a.Color, a.Opacity));
        var y = a.Y;
        foreach (var line in a.Text.Replace("\r", "").Split('\n'))
        {
            g.DrawString(line, font, brush, new XPoint(a.X, y), XStringFormats.TopLeft);
            y += size * 1.2;
        }
    }

    private static XColor Color(string? hex, double opacity)
    {
        var alpha = (int)Math.Round(Math.Clamp(opacity is 0 ? 1 : opacity, 0.05, 1) * 255);
        if (hex is ['#', .. var rest] && rest.Length == 6 && int.TryParse(rest, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return XColor.FromArgb(alpha, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        return XColor.FromArgb(alpha, 220, 0, 0);
    }

    /// <summary>DejaVu Sans из ресурсов сборки — с кириллицей и без зависимости от шрифтов ОС в контейнере.</summary>
    private sealed class EmbeddedFontResolver : IFontResolver
    {
        public const string Family = "DejaVu Sans";
        private const string Face = "DejaVuSans";

        private static readonly Lazy<byte[]> Font = new(() =>
        {
            using var stream = typeof(PdfAnnotator).Assembly.GetManifestResourceStream("Fiit.Api.Assets.DejaVuSans.ttf")
                ?? throw new InvalidOperationException("Font resource not found");
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        });

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) => new(Face);

        public byte[] GetFont(string faceName) => Font.Value;
    }
}
