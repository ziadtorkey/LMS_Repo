using Lms.Domain.Classes;
using Lms.Domain.Courses;
using Lms.Domain.Enrollment;
using Lms.Domain.Teaching;
using Lms.Shared.Classes;
using Lms.Shared.Courses;
using Lms.Shared.Enrollment;
using Lms.Shared.Teaching;

namespace Lms.UnitTests.Courses;

/// <summary>The domain does not reference Lms.Shared, so its status strings are checked against the contract here.</summary>
public sealed class AcademicConstantsParityTests
{
    [Fact]
    public void Domain_status_strings_are_values_of_the_contract_catalogs()
    {
        Assert.Contains(Course.ActiveStatus, CourseStatuses.All);
        Assert.Contains(Course.InactiveStatus, CourseStatuses.All);
        Assert.Contains(Class.ActiveStatus, ClassStatuses.All);
        Assert.Contains(Class.ClosedStatus, ClassStatuses.All);
        Assert.Contains(ClassStudent.ActiveStatus, ClassStudentStatuses.All);
        Assert.Contains(ClassStudent.RemovedStatus, ClassStudentStatuses.All);
        Assert.Contains(ClassStudent.TransferredOutStatus, ClassStudentStatuses.All);
        Assert.Contains(CourseEnrollment.ActiveStatus, EnrollmentStatuses.All);
        Assert.Contains(CourseEnrollment.DroppedStatus, EnrollmentStatuses.All);
        Assert.Contains(ClassCourseTeacher.PrimaryRole, TeacherRoles.All);
        Assert.Contains(ClassCourseTeacher.AssistantRole, TeacherRoles.All);
        Assert.Contains(CourseRestrictionEvaluator.Religion, CourseRestrictionTypes.All);
        Assert.Contains(CourseRestrictionEvaluator.Gender, CourseRestrictionTypes.All);
        Assert.Contains(CourseRestrictionEvaluator.LevelType, CourseRestrictionTypes.All);
    }
}
