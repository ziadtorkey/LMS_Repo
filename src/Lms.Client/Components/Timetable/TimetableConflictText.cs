using Lms.Client.Services;
using Lms.Shared.Timetable;
using Microsoft.Extensions.Localization;

namespace Lms.Client.Components.Timetable;

/// <summary>The server's clashes in plain words. The client never predicts a clash; it only phrases what the server named.</summary>
public static class TimetableConflictText
{
    public static string Describe(IEnumerable<SlotConflictDto> conflicts, IStringLocalizer<Lms.Client.Resources.SharedResource> l, ICultureService culture) =>
        string.Join(" ", conflicts.Select(c =>
        {
            var course = c.CourseNameAr is not null && c.CourseNameEn is not null ? culture.Pick(c.CourseNameAr, c.CourseNameEn) : l["Tt_Clash_Hidden"].Value;
            return c.Kind == ConflictKinds.ClassDoubleBooked
                ? string.Format(l["Tt_Clash_Class"], course)
                : string.Format(l["Tt_Clash_Teacher"], c.TeacherName ?? l["Tt_Clash_Hidden"].Value, course);
        }));
}
