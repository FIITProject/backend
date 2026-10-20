using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Fiit.Api.Services;

/// <summary>Ожидаемая ошибка бизнес-логики → ProblemDetails с понятным текстом и кодом для бота/фронта.</summary>
public class ApiException(int status, string title, string? code = null) : Exception(title)
{
    public int Status { get; } = status;
    public string? Code { get; } = code;

    public static ApiException NotFound(string title = "Не найдено") => new(StatusCodes.Status404NotFound, title);
    public static ApiException Forbidden(string title) => new(StatusCodes.Status403Forbidden, title);
    public static ApiException Conflict(string title, string? code = null) => new(StatusCodes.Status409Conflict, title, code);
    public static ApiException BadRequest(string title, string? code = null) => new(StatusCodes.Status400BadRequest, title, code);

    public static ApiException Archived() =>
        Conflict("Курс в архиве — доступен только для чтения", Contracts.ErrorCodes.CourseArchived);
}

public class ApiExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        if (exception is not ApiException e)
            return false;

        http.Response.StatusCode = e.Status;
        var details = new ProblemDetails { Status = e.Status, Title = e.Message };
        if (e.Code is not null)
            details.Extensions["code"] = e.Code;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = details, Exception = exception });
    }
}
