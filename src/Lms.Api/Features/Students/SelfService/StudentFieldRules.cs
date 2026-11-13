using Lms.Shared.People;

namespace Lms.Api.Features.Students.SelfService;

/// <summary>
/// Which fields a student edits directly and which need approval (FR-STU-04, D27), from the <c>Students.CriticalFields</c> setting.
/// The student code and the account email are in neither set.
/// </summary>
public static class StudentFieldRules
{
    public static (IReadOnlySet<string> Editable, IReadOnlySet<string> Requestable) Classify(IReadOnlyCollection<string> critical)
    {
        var requestable = StudentFields.Requestable.Where(critical.Contains).ToHashSet(StringComparer.Ordinal);
        var editable = StudentFields.Requestable.Where(f => !requestable.Contains(f)).ToHashSet(StringComparer.Ordinal);
        return (editable, requestable);
    }
}
