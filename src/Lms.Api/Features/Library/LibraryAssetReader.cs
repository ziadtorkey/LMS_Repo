using Lms.Api.Infrastructure;
using Lms.Domain.Content;
using Lms.Domain.Library;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Library;

/// <summary>
/// A library asset (owned by a unit, no ClassCourse) is readable through <c>/api/assets/{id}</c> when a resource the caller can
/// read at their own unit uses it as its cover, or as its file while downloads are allowed. A file of a resource with downloads
/// off is only ever streamed by the protected reader.
/// </summary>
internal sealed class LibraryAssetReader(LibraryQueries queries, IHttpContextAccessor accessor) : IAssetReferenceReader
{
    public async Task<bool> CanReadAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        if (asset.ClassCourseId is not null || accessor.HttpContext is not { } http)
        {
            return false;
        }

        var (unit, problem) = await LibraryEndpoints.BrowseUnitAsync(null, http, queries, cancellationToken);
        if (problem is not null)
        {
            return false;
        }

        var visible = queries.Apply(queries.VisibleAt(unit!), new Lms.Shared.Library.LibraryQuery(), await queries.StudentLevelTypesAsync(cancellationToken), unit!.OrganizationId);
        return await visible.AnyAsync(
            r => r.CoverAssetId == asset.Id || (r.FileAssetId == asset.Id && r.DownloadAllowed), cancellationToken);
    }
}
