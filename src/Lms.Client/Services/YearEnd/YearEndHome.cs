namespace Lms.Client.Services.YearEnd;

// M7 H8 (FE proposal #808, D86): the year-end checklist of the home page. PROVISIONAL: the contract has no route for it yet ("the server sends the flag and the step states"),
// so these types live in the client until the Lead's contract PR names them; only this file and the fake change then.

/// <summary>The four steps of a year end, in order (spec m7-year-end section 2).</summary>
public static class YearEndStepKeys
{
    public const string PublishGrades = "PublishGrades";
    public const string CloseYear = "CloseYear";
    public const string RunPromotion = "RunPromotion";
    public const string OpenNextYear = "OpenNextYear";

    public static IReadOnlyList<string> All { get; } = [PublishGrades, CloseYear, RunPromotion, OpenNextYear];
}

/// <summary>Done; NeedsAttention (a count to review); ToDo; Blocked = cannot start yet (the promotion before the close), the line says why.</summary>
public static class YearEndStepStates
{
    public const string Done = "Done";
    public const string NeedsAttention = "NeedsAttention";
    public const string ToDo = "ToDo";
    public const string Blocked = "Blocked";
}

public sealed record YearEndStepDto(string Key, string State, int? Count, DateTime? DoneAtUtc);

/// <summary><c>Show</c> is the server's flag: an Active year within 60 days of its end (D86). The tiles are plain counts.</summary>
public sealed record YearEndChecklistDto(
    bool Show,
    Guid YearId,
    string NameAr,
    string NameEn,
    DateOnly EndDate,
    int DaysLeft,
    IReadOnlyList<YearEndStepDto> Steps,
    int ActiveStudents,
    int Classes,
    int GradeChangesWaiting,
    int UnpublishedGradeItems);

public interface IYearEndHomeApi
{
    Task<YearEndChecklistDto> GetAsync(CancellationToken ct = default);
}

/// <summary>In-memory checklist for the standalone dev server and tests; <see cref="Current"/> is what the home page shows.</summary>
public sealed class FakeYearEndHomeApi : IYearEndHomeApi
{
    public YearEndChecklistDto Current { get; set; } = new(
        true, Guid.Parse("00000000-0000-0000-0000-0000000f0001"), "٢٠٢٦/٢٠٢٧", "2026/2027", new DateOnly(2027, 6, 24), 42,
        [
            new(YearEndStepKeys.PublishGrades, YearEndStepStates.NeedsAttention, 3, null),
            new(YearEndStepKeys.CloseYear, YearEndStepStates.ToDo, null, null),
            new(YearEndStepKeys.RunPromotion, YearEndStepStates.Blocked, null, null),
            new(YearEndStepKeys.OpenNextYear, YearEndStepStates.ToDo, null, null),
        ], 480, 20, 2, 3);

    public Task<YearEndChecklistDto> GetAsync(CancellationToken ct = default) => Task.FromResult(Current);
}
