using Lms.Domain.Abstractions;
using Lms.Domain.Packages;
using Lms.Infrastructure.Persistence;

namespace Lms.Infrastructure.Packages;

/// <summary><see cref="IPackageRevocations"/> on the scoped context: the marker joins the caller's transaction.</summary>
public sealed class PackageRevocationWriter(LmsDbContext db, IClock clock) : IPackageRevocations
{
    public Task RevokeAsync(PackageUpload package, Guid? studentProfileId, CancellationToken ct = default)
    {
        db.Add(new PackageRevocation(package, studentProfileId, clock.UtcNow));
        return Task.CompletedTask;
    }
}
