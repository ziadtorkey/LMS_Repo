using Lms.Api.Infrastructure;
using Lms.Domain.Content;
using Lms.Domain.Forums;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Forums;

/// <summary>
/// An asset attached to a forum post is readable by everyone who can see the forum of its ClassCourse (Forums.View: unit
/// scope and relationship rule), and only through a post that is still there.
/// </summary>
internal sealed class ForumAssetReader(LmsDbContext db, IHttpContextAccessor accessor) : IAssetReferenceReader
{
    public async Task<bool> CanReadAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        if (accessor.HttpContext is not { } http)
        {
            return false;
        }

        if (asset.ClassCourseId is not { } classCourseId)
        {
            return false; // a unit-owned (library) asset is never a forum attachment
        }

        var attached = await db.Set<ForumPostAttachment>().AnyAsync(a => a.AssetId == asset.Id && a.ClassCourseId == classCourseId, cancellationToken);
        return attached && await ForumSupport.HoldsAsync(http, Permissions.Forums.View, classCourseId, cancellationToken);
    }
}
