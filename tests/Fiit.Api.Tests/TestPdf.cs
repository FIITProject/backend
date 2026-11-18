using System.Text;
using PdfSharp.Pdf;

namespace Fiit.Api.Tests;

public static class TestPdf
{
    public static byte[] Create(int pages = 1, Action<PdfDocument>? configure = null)
    {
        using var doc = new PdfDocument();
        for (var i = 0; i < pages; i++)
            doc.AddPage();
        configure?.Invoke(doc);
        using var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        return ms.ToArray();
    }

    /// <summary>PDF, собранный руками: каталог с дополнительными записями <paramref name="catalogExtra"/> и одна пустая страница.</summary>
    public static byte[] Raw(string catalogExtra)
    {
        var objects = new[]
        {
            $"<< /Type /Catalog /Pages 2 0 R {catalogExtra} >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] >>",
        };
        var sb = new StringBuilder("%PDF-1.7\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(sb.ToString()));
            sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = Encoding.ASCII.GetByteCount(sb.ToString());
        sb.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets)
            sb.Append($"{o:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
