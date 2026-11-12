using Lms.Domain.Auditing;
using Lms.Domain.Common;
using Lms.Domain.Courses;

namespace Lms.Domain.Blueprints;

/// <summary>
/// One version of a course blueprint (the reusable lesson plan skeleton, FR-LP-01). A course has at most one Draft
/// (<see cref="DraftKey"/>) and any number of numbered versions; publishing turns the draft into the next version and
/// supersedes the previous one. The unit fields are the course owner unit.
/// </summary>
[Audited]
public sealed class Blueprint : UnitOwnedEntity, ILiveKeyed
{
    public const string DraftStatus = "Draft";
    public const string PublishedStatus = "Published";
    public const string SupersededStatus = "Superseded";

    private Blueprint()
    {
        Status = DraftStatus;
    }

    private Blueprint(Course course)
    {
        PlaceLike(course);
        CourseId = course.Id;
        Status = DraftStatus;
        Version = 0;
        Revision = 1;
        DraftKey = course.Id;
    }

    public Guid CourseId { get; private set; }

    /// <summary>0 while draft, then 1, 2, 3... in publish order.</summary>
    public int Version { get; private set; }

    public string Status { get; private set; }

    /// <summary>Bumped by every draft save; also the concurrency token, so two saves of one revision cannot both win.</summary>
    public int Revision { get; private set; }

    public DateTime? PublishedAt { get; private set; }
    public Guid? PublishedBy { get; private set; }
    public string? ApplyMode { get; private set; }

    /// <summary>The course id while this is the live draft: with the course it makes "one draft per course" unique.</summary>
    public Guid? DraftKey { get; private set; }

    /// <summary>The version number while live and published: with the course it makes versions unique.</summary>
    public int? VersionKey { get; private set; }

    public static Blueprint CreateDraft(Course course) => new(course);

    public void NextRevision() => Revision++;

    /// <summary>Turns the draft into the given version.</summary>
    public void Publish(int version, string applyMode, Guid? publishedBy, DateTime nowUtc)
    {
        if (Status != DraftStatus)
        {
            throw new InvalidOperationException("Only a draft can be published.");
        }

        Version = version;
        VersionKey = version;
        Status = PublishedStatus;
        ApplyMode = applyMode;
        PublishedAt = nowUtc;
        PublishedBy = publishedBy;
        DraftKey = null;
    }

    public void Supersede() => Status = SupersededStatus;

    public void ClearLiveKeys()
    {
        DraftKey = null;
        VersionKey = null;
    }
}
