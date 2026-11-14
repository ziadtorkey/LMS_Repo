using Lms.Domain.Common;

namespace Lms.Domain.Packages;

/// <summary>
/// A revocation marker of the package host (M4b Task 9a, ADR 0005 section 5): tokens issued for the package (and the student, when
/// one is named) before <see cref="RevokedAtUtc"/> stop working. Written when a module is hidden, a student is unenrolled or the
/// package is deleted; the host reads the markers of a package through a short per-instance cache. Unit fields are the package's, so
/// the row is isolated like it.
/// </summary>
public sealed class PackageRevocation : UnitOwnedEntity
{
    private PackageRevocation()
    {
    }

    public PackageRevocation(PackageUpload package, Guid? studentProfileId, DateTime revokedAtUtc)
    {
        PlaceLike(package);
        PackageId = package.Id;
        StudentProfileId = studentProfileId;
        RevokedAtUtc = revokedAtUtc;
    }

    public Guid PackageId { get; private set; }

    /// <summary>The student whose tokens are revoked; null revokes every token of the package.</summary>
    public Guid? StudentProfileId { get; private set; }

    public DateTime RevokedAtUtc { get; private set; }
}

/// <summary>Writes revocation markers on the caller's unit of work (the caller saves).</summary>
public interface IPackageRevocations
{
    /// <summary>Revokes the tokens of one student for the package, or of everyone when <paramref name="studentProfileId"/> is null.</summary>
    Task RevokeAsync(PackageUpload package, Guid? studentProfileId, CancellationToken ct = default);
}
