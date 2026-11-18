using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace Fiit.Api.Services;

public enum PdfVerdict
{
    Ok,
    /// <summary>Не PDF или файл не разбирается.</summary>
    Invalid,
    /// <summary>Защищён паролем — пометки в него не впечатать.</summary>
    Encrypted,
    TooManyPages,
    /// <summary>Скрипты, запуск программ, отправка данных, вложенные файлы.</summary>
    ActiveContent,
}

public record PdfCheck(PdfVerdict Verdict, string? Detail = null);

/// <summary>
/// Проверка сдаваемого PDF до сохранения (С-5.2): файл разбирается тем же PDFsharp, которым потом впечатываются пометки,
/// и не содержит активного содержимого. Эвристика как у pdfid: обычные сканы и выгрузки из Word/LaTeX такого не содержат.
/// </summary>
public static class PdfSafety
{
    public const int MaxPages = 300;
    private const int MaxDepth = 64;

    /// <summary>Ключи, которые несут код или файлы: JavaScript, вложения, Flash/видео, XFA-формы.</summary>
    private static readonly HashSet<string> DangerousKeys = ["/JS", "/JavaScript", "/EmbeddedFile", "/EmbeddedFiles", "/EF", "/RichMedia", "/XFA"];

    /// <summary>Типы действий (/S): выполнить скрипт, запустить программу, отправить или подгрузить данные, открыть вложение.</summary>
    private static readonly HashSet<string> DangerousActions = ["/JavaScript", "/Launch", "/SubmitForm", "/ImportData", "/GoToE", "/Rendition", "/RichMediaExecute"];

    public static PdfCheck Check(byte[] pdf)
    {
        if (pdf.Length < 5 || !pdf.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
            return new(PdfVerdict.Invalid, "no %PDF- header");

        var askedPassword = false;
        PdfDocument document;
        try
        {
            // Modify — как в PdfAnnotator: если откроется здесь, пометки потом тоже впечатаются
            document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Modify, args =>
            {
                askedPassword = true;
                args.Abort = true;
            });
        }
        catch (Exception e)
        {
            return askedPassword ? new(PdfVerdict.Encrypted) : new(PdfVerdict.Invalid, e.Message);
        }

        using (document)
        {
            if (askedPassword || document.SecuritySettings.IsEncrypted)
                return new(PdfVerdict.Encrypted);
            if (document.PageCount == 0)
                return new(PdfVerdict.Invalid, "no pages");
            if (document.PageCount > MaxPages)
                return new(PdfVerdict.TooManyPages, document.PageCount.ToString());

            foreach (var obj in document.Internals.GetAllObjects())
            {
                if (FindActive(obj, 0) is { } hit)
                    return new(PdfVerdict.ActiveContent, hit);
            }
            return new(PdfVerdict.Ok);
        }
    }

    /// <summary>Ищет опасный ключ или тип действия в объекте и вложенных в него прямых объектах. Косвенные объекты проверяются отдельно.</summary>
    private static string? FindActive(PdfItem item, int depth)
    {
        if (depth > MaxDepth)
            return "nesting too deep";
        switch (item)
        {
            case PdfDictionary dict:
                foreach (var (key, value) in dict.Elements)
                {
                    if (DangerousKeys.Contains(key))
                        return key;
                    if (key == "/S" && value is PdfName action && DangerousActions.Contains(action.Value))
                        return action.Value;
                    if (value is not null and not PdfReference && FindActive(value, depth + 1) is { } hit)
                        return hit;
                }
                return null;
            case PdfArray array:
                foreach (var element in array.Elements)
                {
                    if (element is not PdfReference && FindActive(element, depth + 1) is { } hit)
                        return hit;
                }
                return null;
            default:
                return null;
        }
    }
}
