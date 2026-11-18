using Fiit.Contracts;

namespace Fiit.Api.Services;

/// <summary>Пускает в хранилище только безопасный PDF (С-5.2): разбор и поиск активного содержимого, затем антивирус. Текст ошибки бот показывает студенту как есть.</summary>
public class SubmissionFileGuard(IMalwareScanner scanner, ILogger<SubmissionFileGuard> log)
{
    public async Task EnsureSafeAsync(byte[] pdf, string context, CancellationToken ct)
    {
        var check = PdfSafety.Check(pdf);
        switch (check.Verdict)
        {
            case PdfVerdict.Invalid:
                throw ApiException.BadRequest("Нужен файл в формате PDF — этот файл не открывается как PDF", ErrorCodes.NotPdf);
            case PdfVerdict.Encrypted:
                throw ApiException.BadRequest("PDF защищён паролем. Сохраните его без защиты и пришлите снова", ErrorCodes.PdfEncrypted);
            case PdfVerdict.TooManyPages:
                throw ApiException.BadRequest($"Слишком много страниц, максимум {PdfSafety.MaxPages}", ErrorCodes.PdfTooManyPages);
            case PdfVerdict.ActiveContent:
                log.LogWarning("Rejected PDF with active content {Marker} ({Context})", check.Detail, context);
                throw ApiException.BadRequest(
                    "В файле есть активное содержимое (скрипты, вложенные файлы или запуск программ), такой файл сдать нельзя. " +
                    "Сохраните работу заново через «Печать → Сохранить как PDF» и пришлите снова",
                    ErrorCodes.PdfUnsafe);
        }

        ScanResult scan;
        try
        {
            scan = await scanner.ScanAsync(pdf, ct);
        }
        catch (MalwareScanUnavailableException e)
        {
            log.LogError(e, "Malware scan unavailable ({Context})", context);
            throw new ApiException(StatusCodes.Status503ServiceUnavailable, "Не удалось проверить файл, попробуйте прислать его позже", ErrorCodes.ScanUnavailable);
        }
        if (!scan.Clean)
        {
            log.LogWarning("Rejected infected PDF: {Threat} ({Context})", scan.Threat, context);
            throw ApiException.BadRequest("Файл не прошёл антивирусную проверку, такой файл сдать нельзя", ErrorCodes.PdfInfected);
        }
    }
}
