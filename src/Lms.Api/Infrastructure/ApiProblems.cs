namespace Lms.Api.Infrastructure;

/// <summary>Uniform ProblemDetails for expected API failures; every one carries a machine-readable `code` extension.</summary>
public static class ApiProblems
{
    public static IResult NotFound() =>
        Problem(StatusCodes.Status404NotFound, "Not found", "not_found", null, null);

    public static IResult Conflict(string code, string? detail = null, object? extra = null) =>
        Problem(StatusCodes.Status409Conflict, "Conflict", code, detail, extra);

    public static IResult BadRequest(string code, string? detail = null) =>
        Problem(StatusCodes.Status400BadRequest, "Bad request", code, detail, null);

    public static IResult Unprocessable(string code, string? detail = null) =>
        Problem(StatusCodes.Status422UnprocessableEntity, "Unprocessable", code, detail, null);

    public static IResult Forbidden(string code = "forbidden", string? detail = null) =>
        Problem(StatusCodes.Status403Forbidden, "Forbidden", code, detail, null);

    private static IResult Problem(int status, string title, string code, string? detail, object? extra)
    {
        var extensions = new Dictionary<string, object?> { ["code"] = code };
        if (extra is not null)
        {
            extensions["extra"] = extra;
        }

        return Results.Problem(statusCode: status, title: title, detail: detail, extensions: extensions);
    }
}