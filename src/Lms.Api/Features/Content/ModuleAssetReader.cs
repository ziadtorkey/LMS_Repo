using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Content;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Content;

/// <summary>
/// Lets a caller read an asset because a module uses it (M4a Task 5): they hold <c>Modules.View</c> on the asset's class
/// course through the relationship rule, and a student additionally sees a module that references it (published or inside
/// its window, not hidden, for them or their group). Staff with the permission may read any module's asset.
/// </summary>
public sealed class ModuleAssetReader(IHttpContextAccessor accessor, LmsDbContext db, ModuleAccess access, IClock clock) : IAssetReferenceReader
{
    public async Task<bool> CanReadAsync(Asset asset, CancellationToken cancellationToken = default)
    {
        if (accessor.HttpContext is not { } http
            || (await http.CheckClassCourseAsync(Permissions.Modules.View, asset.ClassCourseId, cancellationToken)).Problem is not null)
        {
            return false;
        }

        var referencing = db.Set<ModuleContent>()
            .Where(c => c.AssetId == asset.Id && c.ClassCourseId == asset.ClassCourseId)
            .Select(c => c.ModuleId);
        if (!await access.IsStudentAsync(cancellationToken))
        {
            return await db.Set<LessonModule>().AnyAsync(m => referencing.Contains(m.Id), cancellationToken);
        }

        if (await access.StudentIdAsync(cancellationToken) is not { } studentId)
        {
            return false;
        }

        var group = await access.GroupOfAsync(asset.ClassCourseId, studentId, cancellationToken);
        return await access.VisibleTo(asset.ClassCourseId, group, clock.UtcNow)
            .AnyAsync(p => referencing.Contains(p.Module.Id), cancellationToken);
    }
}
