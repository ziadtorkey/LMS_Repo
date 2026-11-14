using Lms.Domain.Classes;
using Lms.Domain.Packages;
using Lms.Infrastructure.Packages.Attempts;
using Lms.Shared.Packages;

namespace Lms.UnitTests.Packages;

/// <summary>The server-side checks of a tracking commit (ADR 0005 section 4): format, key allow-list and the three size caps.</summary>
public sealed class CommitGateTests
{
    private static PackageUpload Package(string kind = PackageKinds.Scorm, string? version = ScormVersions.Scorm12)
    {
        var upload = (PackageUpload)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(PackageUpload));
        typeof(PackageUpload).GetProperty(nameof(PackageUpload.Kind))!.SetValue(upload, kind);
        typeof(PackageUpload).GetProperty(nameof(PackageUpload.Version))!.SetValue(upload, version);
        return upload;
    }

    private static PackageCommitRequest Commit(string format, params (string Key, string Value)[] data) =>
        new(0, format, data.ToDictionary(d => d.Key, d => d.Value));

    [Fact]
    public void A_valid_scorm_commit_passes_and_format_follows_the_kind()
    {
        Assert.Equal(PackageCommitFormats.Scorm, CommitGate.FormatOf(PackageKinds.Scorm));
        Assert.Equal(PackageCommitFormats.Xapi, CommitGate.FormatOf(PackageKinds.H5p));
        Assert.Equal(CommitRefusal.None, CommitGate.Check(Package(), Commit(PackageCommitFormats.Scorm, ("cmi.core.lesson_status", "passed"), ("cmi.objectives.3.id", "o"))));
    }

    [Fact]
    public void A_wrong_format_an_unknown_key_or_a_key_of_the_other_scorm_version_is_refused()
    {
        Assert.Equal(CommitRefusal.Invalid, CommitGate.Check(Package(), Commit(PackageCommitFormats.Xapi, ("verb", "completed"))));
        Assert.Equal(CommitRefusal.Invalid, CommitGate.Check(Package(), Commit(PackageCommitFormats.Scorm, ("cmi.core.student_name", "x"))));
        Assert.Equal(CommitRefusal.Invalid, CommitGate.Check(Package(), Commit(PackageCommitFormats.Scorm, ("cmi.completion_status", "completed")))); // 2004 key on 1.2
        Assert.Equal(CommitRefusal.None, CommitGate.Check(Package(version: ScormVersions.Scorm2004), Commit(PackageCommitFormats.Scorm, ("cmi.completion_status", "completed"))));
    }

    [Fact]
    public void Value_and_message_caps_are_enforced()
    {
        var big = new string('x', PackageTrackingLimits.MaxValueBytes + 1);
        var ok = new string('x', PackageTrackingLimits.MaxValueBytes);

        Assert.Equal(CommitRefusal.Invalid, CommitGate.Check(Package(), Commit(PackageCommitFormats.Scorm, ("cmi.suspend_data", big))));
        Assert.Equal(CommitRefusal.None, CommitGate.Check(Package(), Commit(PackageCommitFormats.Scorm, ("cmi.suspend_data", ok))));
        Assert.Equal(CommitRefusal.Invalid, CommitGate.Check(Package(), Commit(PackageCommitFormats.Scorm,
            ("cmi.suspend_data", ok), ("cmi.comments", ok), ("cmi.interactions.0.student_response", ok))));
    }

    [Fact]
    public void A_negative_seq_is_refused()
    {
        Assert.Equal(CommitRefusal.Invalid, CommitGate.Check(Package(), new PackageCommitRequest(-1, PackageCommitFormats.Scorm, new Dictionary<string, string>())));
    }

    [Fact]
    public void The_merged_model_replaces_keys_and_is_capped_at_64_kb()
    {
        var merged = CommitGate.Merge(new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" }, new Dictionary<string, string> { ["b"] = "3", ["c"] = "4" });

        Assert.Equal("3", System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(merged!)!["b"]);
        var current = Enumerable.Range(0, 16).ToDictionary(i => "k" + i, _ => new string('x', 4000));
        Assert.NotNull(CommitGate.Merge(current, new Dictionary<string, string>()));
        Assert.Null(CommitGate.Merge(current, new Dictionary<string, string> { ["more"] = new string('y', 4000) }));
    }
}
