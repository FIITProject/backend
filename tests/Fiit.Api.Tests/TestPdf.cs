using PdfSharp.Pdf;

namespace Fiit.Api.Tests;

public static class TestPdf
{
    public static byte[] Create(int pages = 1)
    {
        using var doc = new PdfDocument();
        for (var i = 0; i < pages; i++)
            doc.AddPage();
        using var ms = new MemoryStream();
        doc.Save(ms, closeStream: false);
        return ms.ToArray();
    }
}
