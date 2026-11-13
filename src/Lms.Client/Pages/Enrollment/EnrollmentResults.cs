using Lms.Shared.Enrollment;

namespace Lms.Client.Pages.Enrollment;

/// <summary>What <see cref="ReasonDialog"/> returns: the reason and the effective date.</summary>
public sealed record ReasonResult(string Reason, DateOnly EffectiveOn);

/// <summary>What <c>AddStudentsDialog</c> returns: the server result plus the names of the students that were sent (the result only has ids).</summary>
public sealed record AddStudentsOutcome(AddStudentsResultDto Result, IReadOnlyDictionary<Guid, string> Names);
