namespace Lms.Domain.Grading;

/// <summary>A gradebook rule refused a write. <c>Code</c> is a <c>ProblemCodes</c> value (gradebook_locked, concurrency_conflict, class_closed, ...).</summary>
public sealed class GradebookRuleException(string code, int statusCode = 409) : Exception(code)
{
    public string Code { get; } = code;

    public int StatusCode { get; } = statusCode;
}
