using Lms.Infrastructure.Attendance;

namespace Lms.UnitTests.Attendance;

public sealed class AttendanceTakerTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();
    private static readonly DateOnly Today = new(2027, 1, 7);
    private static readonly IReadOnlyDictionary<Guid, (string? Status, string? Note)> NoRecords = new Dictionary<Guid, (string?, string?)>();

    private static TakePlan Plan(
        string? defaultStatus, TakeMark[] marks, bool taken = false, bool inWindow = true, bool canEdit = false, string? reason = null,
        DateOnly? date = null, IReadOnlyDictionary<Guid, (string? Status, string? Note)>? existing = null) =>
        AttendanceTaker.Plan([A, B, C], existing ?? NoRecords, defaultStatus, marks, taken, inWindow, canEdit, reason, date ?? Today, Today);

    [Fact]
    public void Mark_all_present_with_two_exceptions_in_one_call()
    {
        var plan = Plan("Present", [new TakeMark(B, "Absent", "sick"), new TakeMark(C, "Late", null)]);

        Assert.True(plan.Ok);
        Assert.Equal("Present", plan.Final[A].Status);
        Assert.Equal(("Absent", "sick"), plan.Final[B]);
        Assert.Equal("Late", plan.Final[C].Status);
    }

    [Fact]
    public void Future_session_cannot_be_taken()
    {
        var plan = Plan("Present", [], date: Today.AddDays(1));

        Assert.Equal(("attendance_session_future", 409), (plan.ProblemCode, plan.ProblemStatus));
    }

    [Fact]
    public void Unmarked_rows_are_refused_without_a_default()
    {
        var plan = Plan(null, [new TakeMark(A, "Present", null), new TakeMark(B, "Present", null)]);

        Assert.Equal(("attendance_unmarked_rows", 422), (plan.ProblemCode, plan.ProblemStatus));
    }

    [Fact]
    public void An_earlier_mark_counts_when_no_default_is_given()
    {
        var existing = new Dictionary<Guid, (string?, string?)> { [C] = ("Absent", "n") };

        var plan = Plan(null, [new TakeMark(A, "Present", null), new TakeMark(B, "Late", null)], existing: existing);

        Assert.True(plan.Ok);
        Assert.Equal(("Absent", "n"), plan.Final[C]);
    }

    [Fact]
    public void Unknown_statuses_students_and_duplicates_are_invalid()
    {
        Assert.Equal(400, Plan("Present", [new TakeMark(A, "Gone", null)]).ProblemStatus);
        Assert.Equal(400, Plan("Present", [new TakeMark(Guid.NewGuid(), "Absent", null)]).ProblemStatus);
        Assert.Equal(400, Plan("Present", [new TakeMark(A, "Absent", null), new TakeMark(A, "Late", null)]).ProblemStatus);
        Assert.Equal(400, Plan("Nope", []).ProblemStatus);
    }

    [Fact]
    public void Teacher_edit_after_the_window_is_409()
    {
        var plan = Plan("Present", [], taken: true, inWindow: false, canEdit: false, reason: "typo");

        Assert.Equal(("attendance_edit_window_closed", 409), (plan.ProblemCode, plan.ProblemStatus));
    }

    [Fact]
    public void An_edit_after_the_window_needs_edit_permission_and_a_reason()
    {
        Assert.Equal(("attendance_reason_required", 422), (Plan("Present", [], taken: true, inWindow: false, canEdit: true, reason: " ").ProblemCode, 422));
        Assert.True(Plan("Present", [], taken: true, inWindow: false, canEdit: true, reason: "wrong student").Ok);
        Assert.Equal(422, Plan("Present", [], taken: true, inWindow: false, canEdit: true, reason: new string('x', 501)).ProblemStatus);
    }

    [Fact]
    public void The_window_is_measured_from_the_first_take()
    {
        var taken = new DateTime(2027, 1, 7, 8, 0, 0, DateTimeKind.Utc);

        Assert.True(AttendanceTaker.InWindow(taken, 48, taken.AddHours(48)));
        Assert.False(AttendanceTaker.InWindow(taken, 48, taken.AddHours(48).AddSeconds(1)));
        Assert.True(AttendanceTaker.InWindow(null, 48, taken.AddYears(1))); // an Open session has no window
    }

    [Theory]
    [InlineData(false, true, "Taken")]
    [InlineData(true, true, "Edited")]
    [InlineData(true, false, "EditedAfterWindow")]
    public void Every_change_has_the_right_history_kind(bool wasTaken, bool inWindow, string kind) =>
        Assert.Equal(kind, AttendanceTaker.ChangeKind(wasTaken, inWindow));
}
