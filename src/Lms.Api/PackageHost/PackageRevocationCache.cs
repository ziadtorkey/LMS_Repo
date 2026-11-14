using Lms.Domain.Classes;
using Lms.Domain.Content;
using Lms.Domain.Packages;
using Lms.Infrastructure.Packages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Lms.Api.PackageHost;

/// <summary>
/// The revocation check of the host (ADR 0005 section 5, plan site 8): the markers of a package are loaded under the system context of
/// the token's organization and unit, cached per instance for <see cref="PackageHostOptions.RevocationCacheSeconds"/> (30 s), so there
/// is no per-request database read and the bound is the same on every node. A marker for the student (or for everyone) refuses every
/// token issued before it.
/// </summary>
public sealed class PackageRevocationCache(IMemoryCache cache, PackageSystemContexts contexts, IOptions<PackageHostOptions> options)
{
    private sealed record Marker(Guid? Student, long AtUnix);

    private sealed record State(List<Marker> Markers, bool Available);

    public async Task<bool> IsRevokedAsync(PackageTokenClaims claims, CancellationToken ct)
    {
        var state = await cache.GetOrCreateAsync((claims.Org, claims.Package), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(Math.Max(0, options.Value.RevocationCacheSeconds));
            await using var db = contexts.Create(claims);
            var rows = await db.Set<PackageRevocation>().Where(r => r.PackageId == claims.Package)
                .Select(r => new { r.StudentProfileId, r.RevokedAtUtc }).ToListAsync(ct);
            var markers = rows.Select(r => new Marker(
                r.StudentProfileId, new DateTimeOffset(DateTime.SpecifyKind(r.RevokedAtUtc, DateTimeKind.Utc)).ToUnixTimeSeconds())).ToList();

            // The module of the package must still exist, be inside its visibility window and sit in a lesson and section that are not hidden:
            // window expiry, a module delete on its own and a lesson or section hide take effect within the cache bound without markers.
            var now = DateTime.UtcNow;
            var available = await (from p in db.Set<PackageUpload>()
                                   join m in db.Set<LessonModule>() on p.ModuleId equals m.Id
                                   join l in db.Set<ClassLesson>() on m.ClassLessonId equals l.Id
                                   join s in db.Set<ClassSection>() on l.SectionId equals s.Id
                                   where p.Id == claims.Package && !l.IsHidden && !s.IsHidden
                                         && (m.Visibility == LessonModule.PublishedVisibility
                                             || (m.Visibility == LessonModule.ScheduledVisibility
                                                 && (m.VisibleFromUtc == null || m.VisibleFromUtc <= now)
                                                 && (m.VisibleToUtc == null || now < m.VisibleToUtc)))
                                   select m.Id).AnyAsync(ct);
            return new State(markers, available);
        });

        // A preview token is the teacher's own look at a package that may not be attached or visible yet: only the markers apply to it.
        return state!.Markers.Any(m => (m.Student is null || m.Student == claims.Student) && m.AtUnix >= claims.Iat)
               || (!claims.Preview && !state.Available);
    }
}
