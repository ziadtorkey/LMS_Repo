namespace Lms.Domain.Enrollment;

/// <summary>
/// A business rule of placement or enrollment refused the request. <c>Code</c> is a <c>ProblemCodes</c> value; the API
/// layer answers it with <see cref="StatusCode"/> and, for a failed restriction, the failed types as an extension.
/// </summary>
public sealed class EnrollmentRuleException(string code, int statusCode = 409, IReadOnlyList<string>? failedTypes = null)
    : Exception(code)
{
    public string Code { get; } = code;

    /// <summary>409 for a conflict with the current state, 400 for a request that cannot apply (for example not in class).</summary>
    public int StatusCode { get; } = statusCode;

    public IReadOnlyList<string> FailedTypes { get; } = failedTypes ?? [];
}
