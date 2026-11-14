namespace Lms.Shared.Grading;

/// <summary>One band as a student or guardian sees it in the legend (the scale as configured, never a provisional marker, L23).</summary>
public sealed record BandLegendEntryDto(
    string Code,
    decimal MinPercent,
    decimal MaxPercent,
    string? LabelAr,
    string? LabelEn,
    string? DescriptorAr,
    string? DescriptorEn,
    decimal? Points);

/// <summary>The legend of a course's scale in both languages.</summary>
public sealed record BandLegendDto(string ScaleNameAr, string ScaleNameEn, string Kind, IReadOnlyList<BandLegendEntryDto> Bands);

/// <summary>
/// One published grade item as a student sees it. Hidden and scheduled items do not exist for students: no title, no count, no marker, no
/// "newer score" flag, no rank or class statistics (L6).
/// </summary>
public sealed record PublishedGradeItemDto(
    Guid ItemId,
    string TitleAr,
    string TitleEn,
    Guid? CategoryId,
    decimal? Points,
    decimal MaxPoints,
    bool Excused,
    DateTime PublishedAtUtc);

/// <summary>A category of a student's course view with its result computed from published items only.</summary>
public sealed record StudentGradeCategoryDto(Guid CategoryId, string NameAr, string NameEn, decimal Weight, decimal? Percent);

/// <summary>
/// One course of a student's grades (a term of a ClassCourse). <c>FinalPercent</c>, band and <c>BandPoints</c> (GPA = the band's points for this
/// course; there is no cross-course overall result, L18) are computed by the engine from the published items only. The staff preview
/// (<c>GET …/gradebook/students/{sid}/student-view</c>, <c>Grades.Manage</c> + relationship) returns this same shape from the same helper.
/// </summary>
public sealed record StudentCourseGradesDto(
    Guid ClassCourseId,
    Guid GradebookId,
    string CourseNameAr,
    string CourseNameEn,
    Guid TermId,
    string TermNameAr,
    string TermNameEn,
    int DecimalPlaces,
    IReadOnlyList<PublishedGradeItemDto> Items,
    IReadOnlyList<StudentGradeCategoryDto> Categories,
    decimal? FinalPercent,
    string? BandCode,
    string? BandLabelAr,
    string? BandLabelEn,
    decimal? BandPoints,
    BandLegendDto Legend);

/// <summary>
/// GET <c>/api/me/grades?termId=</c> (<c>Grades.ViewOwn</c>, the caller is a student) and GET <c>/api/students/{sid}/grades?termId=</c>
/// (<c>Grades.ViewOwn</c>, an active guardian link; another guardian's child is 404): published grades only, per course.
/// </summary>
public sealed record StudentGradesDto(
    Guid StudentId,
    string StudentNameAr,
    string StudentNameEn,
    IReadOnlyList<StudentCourseGradesDto> Courses,
    DateTime ComputedAtUtc,
    DateTime ServerNowUtc);
