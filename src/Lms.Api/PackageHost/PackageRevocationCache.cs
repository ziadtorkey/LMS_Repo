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

    public async Task<bool> IsRevokedAsync(PackageTokenClaims claims, CancellationToken ct)
    {
        var markers = await cache.GetOrCreateAsync((claims.Org, claims.Package), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(Math.Max(0, options.Value.RevocationCacheSeconds));
            await using var db = contexts.Create(claims);
            var rows = await db.Set<PackageRevocation>().Where(r => r.PackageId == claims.Package)
                .Select(r => new { r.StudentProfileId, r.RevokedAtUtc }).ToListAsync(ct);
            return rows.Select(r => new Marker(
                r.StudentProfileId, new DateTimeOffset(DateTime.SpecifyKind(r.RevokedAtUtc, DateTimeKind.Utc)).ToUnixTimeSeconds())).ToList();
        });

        return markers!.Any(m => (m.Student is null || m.Student == claims.Student) && m.AtUnix >= claims.Iat);
    }
}
