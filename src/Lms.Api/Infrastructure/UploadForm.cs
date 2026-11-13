using Lms.Api.Features.Files;
using Microsoft.AspNetCore.Http.Features;

namespace Lms.Api.Infrastructure;

/// <summary>
/// Reads a multipart upload with the transport ceiling lifted (the real per-unit limit is applied after the file is read,
/// see <see cref="FileEndpoints.TransportLimitBytes"/>). The caller has already passed authentication and the permission
/// filter, so refused callers never make the server buffer a body.
/// </summary>
internal static class UploadForm
{
    public static async Task<(IFormCollection? Form, IResult? Problem)> ReadAsync(HttpContext http, CancellationToken ct)
    {
        var request = http.Request;
        if (!request.HasFormContentType)
        {
            return (null, ApiProblems.BadRequest("validation_failed", "A multipart form with a file is required."));
        }

        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodyLimit)
        {
            bodyLimit.MaxRequestBodySize = FileEndpoints.TransportLimitBytes;
        }

        try
        {
            http.Features.Set<IFormFeature>(new FormFeature(request, new FormOptions
            {
                MultipartBodyLengthLimit = FileEndpoints.TransportLimitBytes,
            }));
            return (await request.ReadFormAsync(ct), null);
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException or IOException)
        {
            return (null, ApiProblems.BadRequest("validation_failed", "The multipart form is malformed or too large."));
        }
    }
}
