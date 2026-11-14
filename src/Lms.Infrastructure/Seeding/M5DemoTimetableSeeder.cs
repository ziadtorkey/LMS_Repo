using Lms.Domain.Attendance;
using Lms.Domain.Excuses;
using Lms.Domain.Timetable;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Attendance;
using Lms.Shared.Excuses;

namespace Lms.Infrastructure.Seeding;

/// <summary>
/// The timetable and attendance half of the M5 demo (school only): five periods, a published conflict-free week for the demo class course,
/// attendance sessions for the last three weeks with records, one pending excuse of a guardian and one student over the alert threshold.
/// It needs the sample teacher account (the slots name a teacher); without one nothing is added.
/// </summary>
internal static class M5DemoTimetableSeeder
{
    private static readonly (string Ar, string En, int Start, int End)[] PeriodTimes =
    [
        ("الحصة الأولى", "Period 1", 480, 525), ("الحصة الثانية", "Period 2", 535, 580), ("الحصة الثالثة", "Period 3", 600, 645),
        ("الحصة الرابعة", "Period 4", 655, 700), ("الحصة الخامسة", "Period 5", 710, 755),
    ];

    public static async Task SeedAsync(LmsDbContext db, DevSeeder.SeededClient school, M5DemoSeeder.Demo demo, CancellationToken ct)
    {
        if (await M5DemoSeeder.UserIdAsync(db, "teacher@altagamo3.test", ct) is not { } teacher)
        {
            return;
        }

        var periods = PeriodTimes.Select((p, i) => new Period(
            demo.YearUnit, p.Ar, p.En, TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(p.Start)), TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(p.End)), i + 1)).ToList();
        db.AddRange(periods);
        await db.SaveChangesAsync(ct);

        // One class course, so two slots never share a day and period; the teacher teaches it only once at a time: conflict free by construction.
        var plan = new (DayOfWeek Day, int Period)[]
        {
            (DayOfWeek.Sunday, 0), (DayOfWeek.Monday, 0), (DayOfWeek.Tuesday, 0), (DayOfWeek.Tuesday, 2),
            (DayOfWeek.Wednesday, 0), (DayOfWeek.Thursday, 0), (DayOfWeek.Thursday, 2),
        };
        var slots = plan.Select(p => new TimetableSlot(demo.Class, demo.ClassCourse.Id, demo.First.Id, teacher, p.Day, periods[p.Period].Id, "101")).ToList();
        foreach (var slot in slots)
        {
            slot.Publish();
        }

        db.AddRange(slots);
        await db.SaveChangesAsync(ct);

        await AttendanceAsync(db, demo, slots, periods, teacher, ct);
    }

    private static async Task AttendanceAsync(
        LmsDbContext db, M5DemoSeeder.Demo demo, List<TimetableSlot> slots, List<Period> periods, Guid teacher, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var sessions = new List<AttendanceSession>();
        for (var date = today.AddDays(-21); date <= today; date = date.AddDays(1))
        {
            if (date < demo.First.StartDate)
            {
                continue;
            }

            foreach (var slot in slots.Where(s => s.Day == date.DayOfWeek))
            {
                sessions.Add(new AttendanceSession(demo.ClassCourse, date, slot.Id, slot.PeriodId, AttendanceSession.TimetableSource));
            }
        }

        db.AddRange(sessions);
        await db.SaveChangesAsync(ct);

        // Past sessions are taken; today's stay open so the "take attendance" page has something to do.
        var taken = sessions.Where(s => s.Date < today).OrderBy(s => s.Date).ThenBy(s => s.PeriodId).ToList();
        if (taken.Count > 0 && sessions.All(s => s.Date != today))
        {
            taken.RemoveAt(taken.Count - 1); // today is not a school day: the latest lesson stays open instead
        }

        var khalid = demo.Students[1];
        var sara = demo.Students[0];
        AttendanceSession? saraAbsence = null;
        for (var n = 0; n < taken.Count; n++)
        {
            var session = taken[n];
            foreach (var student in demo.Students)
            {
                var status = AttendanceStatuses.Present;
                if (student.Id == khalid.Id)
                {
                    status = n < 6 ? AttendanceStatuses.Absent : n < 8 ? AttendanceStatuses.Late : status; // 6 absences and 2 lates: over the threshold
                }
                else if (student.Id == sara.Id && n == taken.Count - 1)
                {
                    status = AttendanceStatuses.Absent;
                    saraAbsence = session;
                }
                else if (student.Id == demo.Students[2].Id && n == 1)
                {
                    status = AttendanceStatuses.Late;
                }

                db.Add(new AttendanceRecord(session, student.Id, status, null));
            }

            session.RecordSave(teacher, now);
        }

        await db.SaveChangesAsync(ct);

        db.Add(new AttendanceAlert(khalid, absences: 6, lates: 2, windowDays: 30, score: ThresholdCalculator.Score(6, 2, 50), threshold: 5m, now));
        if (saraAbsence is not null && await M5DemoSeeder.UserIdAsync(db, "guardian@altagamo3.test", ct) is { } guardianUser)
        {
            var excuse = new AbsenceExcuse(sara, guardianUser, ExcuseReasons.Illness, "حرارة مرتفعة", saraAbsence.Date, saraAbsence.Date, null, now);
            db.Add(excuse);
            await db.SaveChangesAsync(ct);
            db.Add(new AbsenceExcuseSession(excuse, saraAbsence.Id));
        }

        await db.SaveChangesAsync(ct);
    }
}
