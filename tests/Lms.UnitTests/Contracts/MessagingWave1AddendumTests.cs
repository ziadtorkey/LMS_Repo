using Lms.Shared.Authorization;
using Lms.Shared.Forums;
using Lms.Shared.Messaging;

namespace Lms.UnitTests.Contracts;

/// <summary>M6 wave 1 addenda (#724): the rules problem field format and the course-board source marker.</summary>
public sealed class MessagingWave1AddendumTests
{
    [Fact]
    public void A_refused_rules_cell_is_named_From_greater_than_To()
    {
        Assert.Equal("Student>Teacher", MessagingRuleDefaults.CellField(BuiltInRoles.Student, BuiltInRoles.Teacher));
        Assert.Equal("Teacher>Student", MessagingRuleDefaults.CellField(BuiltInRoles.Teacher, BuiltInRoles.Student));
    }

    [Fact]
    public void The_course_board_item_defaults_to_the_board_and_a_school_entry_is_marked()
    {
        Assert.Equal(["Announcement", "CourseBoard"], AnnouncementSources.All);
        var board = new CourseAnnouncementDto(Guid.NewGuid(), Guid.NewGuid(), "t", "<p>b</p>", false, Guid.NewGuid(), "م", "A", DateTime.UtcNow, null);
        var school = board with { Source = AnnouncementSources.Announcement };

        Assert.Equal(AnnouncementSources.CourseBoard, board.Source);
        Assert.Equal("Announcement", school.Source);
    }
}
