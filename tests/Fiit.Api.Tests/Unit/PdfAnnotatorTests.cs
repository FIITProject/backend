using Fiit.Api.Services;
using PdfSharp.Pdf.IO;

namespace Fiit.Api.Tests.Unit;

public class PdfAnnotatorTests
{
    private const string Annotations = """
        [
          {"type":"stroke","page":0,"color":"#d00000","width":2,"opacity":1,"points":[10,10,50,60,90,20]},
          {"type":"stroke","page":0,"color":"#ffee00","width":14,"opacity":0.4,"points":[100,100]},
          {"type":"text","page":0,"x":40,"y":200,"text":"Молодец!\nНо проверь знак","color":"#0000ff","size":14},
          {"type":"text","page":5,"x":1,"y":1,"text":"страницы нет","size":12}
        ]
        """;

    [Fact]
    public void Burns_annotations_into_a_valid_pdf()
    {
        var original = TestPdf.Create(pages: 2);
        var parsed = PdfAnnotator.Parse(Annotations);
        Assert.NotNull(parsed);

        var result = PdfAnnotator.Apply(original, parsed);

        Assert.True(result.Length > original.Length);
        using var doc = PdfReader.Open(new MemoryStream(result), PdfDocumentOpenMode.Import);
        Assert.Equal(2, doc.PageCount);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""[{"type":"circle","page":0}]""")]
    [InlineData("""[{"type":"stroke","page":-1}]""")]
    public void Rejects_invalid_json(string json) => Assert.Null(PdfAnnotator.Parse(json));

    [Fact]
    public void Empty_list_is_valid() => Assert.Empty(PdfAnnotator.Parse("[]")!);
}
