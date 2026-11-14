using Lms.Domain.Attendance;
using Lms.Shared.Attendance;

namespace Lms.UnitTests.Attendance;

public sealed class AttendanceConstantsTests
{
    [Fact]
    public void Domain_values_equal_the_contract_catalogs()
    {
        Assert.Equal(AttendanceSources.Timetable, AttendanceSession.TimetableSource);
        Assert.Equal(AttendanceSources.AdHoc, AttendanceSession.AdHocSource);
        Assert.Equal(AttendanceSources.Live, AttendanceSession.LiveSource);
        Assert.Equal(AttendanceSessionStates.Open, AttendanceSession.OpenState);
        Assert.Equal(AttendanceSessionStates.Taken, AttendanceSession.TakenState);
    }
}
