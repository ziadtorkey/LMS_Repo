using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Files;
using Lms.Domain.Imports;
using Lms.Api.Features.Imports;
using Lms.Shared.Authorization;
using Lms.Shared.Imports;

namespace Lms.Api.Features.Students.Import;

/// <summary>
/// GET /api/imports/students/{id}/credentials: the one-time sheet of the accounts a student import created (D24). Only the
/// requester of a committed batch, within 15 minutes of the commit, and only once; everything else is 404.
/// </summary>
internal static class CredentialsSheetEndpoint
{
    public const string Pattern = "/api/imports/" + ImportKinds.Students + "/{id:guid}/credentials";

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet(Pattern, HandleAsync).RequireAuthorization().RequirePermission(Permissions.Students.Import);

    private static async Task<IResult> HandleAsync(
        Guid id, HttpContext http, ImportQueries queries, ImportCredentialsStore store, ICurrentContext context, CancellationToken ct)
    {
        var batch = await queries.FindAsync(ImportKinds.Students, id, ct);
        if (batch is null)
        {
            return ApiProblems.NotFound();
        }

        var access = await http.CheckUnitAsync(Permissions.Students.Import, batch.OrgUnitId, ct);
        if (access.Problem is not null)
        {
            return access.Problem;
        }

        if (batch.Status != ImportStatus.Committed || batch.RequestedBy != context.UserId || store.Take(id, context.UserId) is not { } sheet)
        {
            return ApiProblems.NotFound();
        }

        var buffer = new MemoryStream();
        ImportCredentialsStore.Write(sheet, buffer);
        buffer.Position = 0;
        http.Response.Headers.CacheControl = "no-store";
        return Results.File(buffer, FileContentTypes.ForExtension(".xlsx"), "students-credentials.xlsx");
    }
}
