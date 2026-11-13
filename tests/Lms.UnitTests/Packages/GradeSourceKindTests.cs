using Lms.Domain.Grading;

namespace Lms.UnitTests.Packages;

/// <summary>The enum is stored as an int in GradeEvents: the values are a contract and never change (M4b Tasks 10 and 11 add Scorm and H5p).</summary>
public sealed class GradeSourceKindTests
{
    [Fact]
    public void Values_are_pinned_because_they_are_stored_as_integers() =>
        Assert.Equal(
            [(1, "Assignment"), (2, "Quiz"), (3, "Scorm"), (4, "H5p")],
            Enum.GetValues<GradeSourceKind>().OrderBy(k => (int)k).Select(k => ((int)k, k.ToString())));
}
