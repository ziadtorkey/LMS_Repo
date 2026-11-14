namespace Lms.Shared.Grading;

/// <summary>
/// Query of <c>GET /api/grade-reports/{kind}</c> (<see cref="GradeReportKinds"/>: <c>student</c> needs <c>StudentId</c>, <c>class</c> needs
/// <c>ClassId</c>, <c>course</c> needs <c>ClassCourseId</c>). <c>TermId</c> null = the current term.
/// </summary>
public sealed record GradeReportQuery(
    Guid? StudentId = null,
    Guid? ClassId = null,
    Guid? ClassCourseId = null,
    Guid? TermId = null,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize);

/// <summary>One row of a grade report (a student in a course). Published and unpublished items both count for staff; the engine is the one the grid uses.</summary>
public sealed record GradeReportRowDto(
    Guid StudentId,
    string StudentNameAr,
    string StudentNameEn,
    string StudentNumber,
    Guid ClassCourseId,
    string CourseNameAr,
    string CourseNameEn,
    decimal? FinalPercent,
    string? BandCode,
    string? BandLabelAr,
    string? BandLabelEn,
    decimal? BandPoints);

/// <summary>
/// GET <c>/api/grade-reports/{student|class|course}</c> and <c>…/export</c> (Excel with the "Scale" sheet, L27) (<c>Grades.ViewReports</c>;
/// Supervisor their courses, Manager unit scope; Teacher and Counselor 403). <c>ComputedAtUtc</c> says when the numbers were computed.
/// </summary>
public sealed record GradeReportDto(
    string Kind,
    PagedResult<GradeReportRowDto> Rows,
    DateTime ComputedAtUtc,
    DateTime ServerNowUtc);
