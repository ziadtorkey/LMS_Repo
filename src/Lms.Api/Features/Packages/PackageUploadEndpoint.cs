using System.Text;
using Lms.Api.Features.Enrollment.Groups;
using Lms.Api.Features.Files;
using Lms.Api.Infrastructure;
using Lms.Domain.Files;
using Lms.Infrastructure.Configuration;
using Lms.Infrastructure.Enrollment;
using Lms.Infrastructure.Packages;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.Configuration;
using Lms.Shared.Packages;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Lms.Api.Features.Packages;

/// <summary>
/// POST /api/class-courses/{id}/packages (M4b Task 8a). The checks come before the body is read: the permission and relationship
/// (404 on a miss), the <c>ScormContent</c> flag (403), the content host (409 <c>package_host_not_configured</c>, D56) and the closed
/// class / year guard. The form carries <c>lessonKey</c> and <c>kind</c> before the <c>file</c> part; the file is copied to a temp file,
/// stopping at <c>Files.MaxUploadMb</c>, then stored. Answers 202 with the upload in <c>Uploaded</c>; validation is the job of Task 8b.
/// </summary>
internal static class PackageUploadEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost(Routes.ClassCoursePackagesPattern, UploadAsync).RequireAuthorization().RequirePermission(Permissions.Packages.Upload);

    private static async Task<IResult> UploadAsync(
        Guid id, HttpContext http, LmsDbContext db, ISettingsResolver settings, IOptions<PackageHostOptions> host, PlacementGuard guard,
        PackageService packages, CancellationToken ct)
    {
        var (classCourse, problem) = await http.CheckClassCourseAsync(Permissions.Packages.Upload, id, ct);
        if (problem is not null)
        {
            return problem;
        }

        if (await PackageSupport.FlagAsync(classCourse!, settings, ct) is { } disabled)
        {
            return disabled;
        }

        if (PackageSupport.HostProblem(http, host) is { } notConfigured)
        {
            return notConfigured;
        }

        if (await GroupEndpoints.WritableAsync(classCourse!, db, guard, ct) is { } closed)
        {
            return closed;
        }

        if (!MediaTypeHeaderValue.TryParse(http.Request.ContentType, out var mediaType)
            || !string.Equals(mediaType.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase)
            || HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value is not { Length: > 0 } boundary)
        {
            return PackageSupport.Invalid("body", "multipart_required");
        }

        var maxBytes = (await settings.ResolveAsync(SettingKeys.FilesMaxUploadMb, classCourse!.OrgUnitPath, ct)).Value * 1024L * 1024L;
        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodyLimit)
        {
            bodyLimit.MaxRequestBodySize = maxBytes + 1024 * 1024;
        }

        var reader = new MultipartReader(boundary, http.Request.Body) { BodyLengthLimit = maxBytes + 1024 * 1024 };
        string? tempPath = null;
        try
        {
            string? lessonText = null, kind = null;
            MultipartSection? file = null;
            string? fileName = null;
            while (await reader.ReadNextSectionAsync(ct) is { } section)
            {
                if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition))
                {
                    return PackageSupport.Invalid("body", "malformed");
                }

                var name = HeaderUtilities.RemoveQuotes(disposition.Name).Value;
                if (name == PackageUploadForm.File)
                {
                    file = section;
                    fileName = HeaderUtilities.RemoveQuotes(disposition.FileName).Value;
                    break;
                }

                using var text = new StreamReader(section.Body, Encoding.UTF8, leaveOpen: true);
                var value = (await text.ReadToEndAsync(ct)).Trim();
                if (name == PackageUploadForm.LessonKey)
                {
                    lessonText = value;
                }
                else if (name == PackageUploadForm.Kind)
                {
                    kind = value;
                }
            }

            if (!Guid.TryParse(lessonText, out var lessonKey))
            {
                return PackageSupport.Invalid(PackageUploadForm.LessonKey, "required_before_file");
            }

            if (kind is null || !PackageKinds.All.Contains(kind))
            {
                return PackageSupport.Invalid(PackageUploadForm.Kind, "invalid_value");
            }

            if (file is null || string.IsNullOrEmpty(fileName))
            {
                return PackageSupport.Invalid(PackageUploadForm.File, "required");
            }

            fileName = Path.GetFileName(fileName);
            if (fileName.Length > 255)
            {
                fileName = fileName[^255..];
            }

            if (!PackageService.ExtensionMatches(kind, fileName))
            {
                return PackageSupport.Invalid(PackageUploadForm.File, "wrong_extension_for_kind");
            }

            if (!await PackageSupport.LessonExistsAsync(db, classCourse.Id, lessonKey, ct))
            {
                return ApiProblems.NotFound();
            }

            tempPath = Path.GetTempFileName();
            long length;
            await using (var temp = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                length = await CopyLimitedAsync(file.Body, temp, maxBytes, ct);
            }

            if (length > maxBytes)
            {
                return TooLarge(maxBytes);
            }

            if (length == 0)
            {
                return PackageSupport.Invalid(PackageUploadForm.File, "empty");
            }

            await using var stream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            var upload = await packages.CreateAsync(classCourse, lessonKey, kind, fileName, length, stream, ct);
            return Results.Accepted(Routes.Package(upload.Id), PackageSupport.ToDto(upload));
        }
        catch (FileTooLargeException ex)
        {
            return TooLarge(ex.MaxBytes);
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException or IOException)
        {
            return PackageSupport.Invalid("body", "malformed_or_too_large");
        }
        finally
        {
            if (tempPath is not null)
            {
                File.Delete(tempPath);
            }
        }
    }

    /// <summary>Copies up to <paramref name="max"/> bytes; returns max + 1 as soon as the source has more.</summary>
    private static async Task<long> CopyLimitedAsync(Stream source, Stream target, long max, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > max)
            {
                return max + 1;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        return total;
    }

    private static IResult TooLarge(long maxBytes) => Results.Problem(
        statusCode: StatusCodes.Status413PayloadTooLarge,
        title: "Payload too large",
        extensions: new Dictionary<string, object?> { ["code"] = ProblemCodes.package_too_large, ["maxBytes"] = maxBytes });
}
