using Lms.Domain.Timetable;

namespace Lms.UnitTests.Timetable;

public sealed class ConflictCheckerTests
{
    private static readonly Guid ClassA = Guid.NewGuid();
    private static readonly Guid ClassB = Guid.NewGuid();
    private static readonly Guid Teacher1 = Guid.NewGuid();
    private static readonly Guid Teacher2 = Guid.NewGuid();
    private static readonly Guid P1 = Guid.NewGuid();
    private static readonly Guid P2 = Guid.NewGuid();

    private static SlotPlace Place(Guid cls, Guid teacher, DayOfWeek day, Guid period, Guid? id = null) => new(id ?? Guid.NewGuid(), cls, teacher, day, period);

    [Fact]
    public void A_teacher_in_two_classes_at_the_same_time_clashes()
    {
        var other = Place(ClassB, Teacher1, DayOfWeek.Sunday, P1);

        var clashes = ConflictChecker.Find(Place(ClassA, Teacher1, DayOfWeek.Sunday, P1), [other]);

        Assert.Equal([new SlotClash(ConflictChecker.TeacherDoubleBooked, other.SlotId)], clashes);
    }

    [Fact]
    public void A_class_with_two_lessons_at_the_same_time_clashes()
    {
        var other = Place(ClassA, Teacher2, DayOfWeek.Sunday, P1);

        var clashes = ConflictChecker.Find(Place(ClassA, Teacher1, DayOfWeek.Sunday, P1), [other]);

        Assert.Equal([new SlotClash(ConflictChecker.ClassDoubleBooked, other.SlotId)], clashes);
    }

    [Fact]
    public void The_same_teacher_and_class_clashes_both_ways()
    {
        var other = Place(ClassA, Teacher1, DayOfWeek.Sunday, P1);

        var kinds = ConflictChecker.Find(Place(ClassA, Teacher1, DayOfWeek.Sunday, P1), [other]).Select(c => c.Kind);

        Assert.Equal([ConflictChecker.TeacherDoubleBooked, ConflictChecker.ClassDoubleBooked], kinds);
    }

    [Theory]
    [InlineData(DayOfWeek.Monday, true)]   // another day
    [InlineData(DayOfWeek.Sunday, false)]  // another period (set below)
    public void Another_day_or_period_never_clashes(DayOfWeek day, bool samePeriod)
    {
        var other = Place(ClassA, Teacher1, day, samePeriod ? P1 : P2);

        Assert.Empty(ConflictChecker.Find(Place(ClassA, Teacher1, DayOfWeek.Sunday, P1), [other]));
    }

    [Fact]
    public void A_slot_never_clashes_with_itself_and_unrelated_slots_do_not_clash()
    {
        var self = Place(ClassA, Teacher1, DayOfWeek.Sunday, P1);
        var unrelated = Place(ClassB, Teacher2, DayOfWeek.Sunday, P1);

        Assert.Empty(ConflictChecker.Find(self, [self, unrelated]));
    }
}
