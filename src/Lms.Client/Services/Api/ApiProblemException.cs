namespace Lms.Client.Services.Api;

/// <summary>
/// An RFC 7807 error answered by the API. <see cref="Code"/> is the ProblemCodes value
/// (or the access-denial reason, e.g. BlockedByPolicy); <see cref="Detail"/> is server-localized text.
/// </summary>
public sealed class ApiProblemException(int status, string? code, string? detail, IReadOnlyDictionary<string, string[]>? errors)
    : HttpRequestException($"API error {status} {code}", null, (System.Net.HttpStatusCode)status)
{
    public int Status { get; } = status;
    public string? Code { get; } = code;
    public string? Detail { get; } = detail;
    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;
}
