using System.Text.Json;
using Lms.Domain.Classes;
using Lms.Domain.Common;

namespace Lms.Domain.Packages;

/// <summary>
/// One run of a SCORM or H5P package by one student (M4b Task 9b, FR-STD-01/02). The data model the content reported over the page's
/// private port is kept as a flat key → value map (<see cref="Model"/>, at most 64 KB) and only the highest <c>seq</c> counts. An attempt
/// is <c>InProgress</c> until a <c>finish</c> finds the module's completion rule met; opening a completed package again starts a new attempt (D57).
/// Not audited row by row (volume). Unit fields are the package's.
/// </summary>
public sealed class PackageAttempt : UnitOwnedEntity
{
    public const string InProgressStatus = "InProgress";
    public const string CompletedStatus = "Completed";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private PackageAttempt()
    {
        Status = Format = DataJson = string.Empty;
    }

    public PackageAttempt(PackageUpload package, Guid moduleId, Guid studentId, string format, DateTime nowUtc)
    {
        PlaceInPackage(package);
        PackageId = package.Id;
        ClassCourseId = package.ClassCourseId;
        ModuleId = moduleId;
        StudentId = studentId;
        Format = format;
        Status = InProgressStatus;
        OpenMarker = 1;
        LastSeq = -1;
        DataJson = "{}";
        StartedAtUtc = Micros(nowUtc);
    }

    public Guid PackageId { get; private set; }
    public Guid ClassCourseId { get; private set; }
    public Guid ModuleId { get; private set; }
    public Guid StudentId { get; private set; }

    /// <summary>A <c>PackageAttemptStatuses</c> value.</summary>
    public string Status { get; private set; }

    /// <summary>1 while the attempt is open, null once it is completed: with the package and the student it is unique, so a student has one open attempt per package.</summary>
    public int? OpenMarker { get; private set; }

    /// <summary>A <c>PackageCommitFormats</c> value: how the content reports (<c>Scorm</c> or <c>Xapi</c>).</summary>
    public string Format { get; private set; }

    /// <summary>The highest commit <c>seq</c> applied; -1 before the first commit.</summary>
    public long LastSeq { get; private set; }

    /// <summary>The data model as JSON (a flat key → value map).</summary>
    public string DataJson { get; private set; }

    public decimal? ScoreRaw { get; private set; }
    public decimal? ScoreMin { get; private set; }
    public decimal? ScoreMax { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime? LastCommitAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    /// <summary>Optimistic concurrency token: concurrent commits of one attempt never overwrite each other.</summary>
    public int Revision { get; private set; }

    public bool IsOpen => Status == InProgressStatus;

    public Dictionary<string, string> Model => JsonSerializer.Deserialize<Dictionary<string, string>>(DataJson, Json) ?? [];

    /// <summary>Applies one commit: <paramref name="serializedModel"/> is the merged data model. The caller has already checked <c>seq</c>, size and rate.</summary>
    public void ApplyCommit(long seq, string serializedModel, DateTime nowUtc)
    {
        LastSeq = seq;
        DataJson = serializedModel;
        LastCommitAtUtc = Micros(nowUtc);
        Revision++;
    }

    public void SetScore(decimal? raw, decimal? min, decimal? max)
    {
        ScoreRaw = raw;
        ScoreMin = min;
        ScoreMax = max;
        Revision++;
    }

    /// <summary>Closes the attempt: the module's completion rule was met.</summary>
    public void Complete(DateTime nowUtc)
    {
        Status = CompletedStatus;
        OpenMarker = null;
        CompletedAtUtc = Micros(nowUtc);
        Revision++;
    }

    public static string Serialize(IReadOnlyDictionary<string, string> model) => JsonSerializer.Serialize(model, Json);

    private void PlaceInPackage(PackageUpload package) => PlaceIn(package.OrganizationId, package.OrgUnitId, package.OrgUnitPath);

    /// <summary>MySql keeps microseconds: the value the caller sees is the value that is stored.</summary>
    private static DateTime Micros(DateTime value) => new(value.Ticks - (value.Ticks % 10), DateTimeKind.Utc);
}
