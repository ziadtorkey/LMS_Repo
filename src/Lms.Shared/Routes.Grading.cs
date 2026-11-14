namespace Lms.Shared;

// M5 wave 1: grading scales, weight templates, gradebook, publishing, grade reads and reports (M5 plan Task 1; the "Route, permission and
// relationship table" is the oracle, Task 40 tests every row). A relationship miss is 404, a missing permission 403. Teacher: own ClassCourses
// with an assignment active today (L15); Supervisor: supervised courses; Student: self; Guardian: an active child link.
public static partial class Routes
{
    /// <summary>
    /// GET (Grades.View; the scales visible at the unit incl. its ancestors', read only) and POST (<c>SaveGradingScaleRequest</c> → <c>GradingScaleDto</c>,
    /// <c>Grades.ConfigureScales</c>, <c>UnitAccess</c>); teacher 403, another school's admin 404; bands that leave a gap 400 <c>scale_bands_invalid</c>.
    /// </summary>
    public const string OrgUnitGradingScalesPattern = OrgUnitPattern + "/grading-scales";

    /// <summary>
    /// GET (Grades.View) → <c>GradingScaleDto</c>; PUT (<c>SaveGradingScaleRequest</c>) and DELETE — Grades.ConfigureScales; an inherited scale is read
    /// only (404 for PUT/DELETE at the descendant); DELETE of a scale a gradebook uses is 409 <c>scale_in_use</c>.
    /// </summary>
    public const string GradingScalePattern = OrgUnitGradingScalesPattern + "/{sid:guid}";

    /// <summary>POST (no body) — Grades.ConfigureScales: an admin confirms a provisional scale (L7); audited.</summary>
    public const string GradingScaleConfirmPattern = GradingScalePattern + "/confirm";

    /// <summary>POST (no body) — Grades.ConfigureScales: makes it the unit's default scale (one default per unit).</summary>
    public const string GradingScaleDefaultPattern = GradingScalePattern + "/default";

    /// <summary>POST (<c>CopyScalePresetRequest</c> → <c>GradingScaleDto</c>) — Grades.ConfigureScales: a provisional copy of a Saudi preset template, or the update of an unedited copy whose <c>UpdateAvailable</c> is set.</summary>
    public const string OrgUnitGradingScalePresetsPattern = OrgUnitGradingScalesPattern + "/presets";

    /// <summary>GET (Grades.View) and POST (<c>SaveWeightTemplateRequest</c> → <c>WeightTemplateDto</c>, Grades.ConfigureScales, <c>UnitAccess</c>); teacher POST 403.</summary>
    public const string OrgUnitWeightTemplatesPattern = OrgUnitPattern + "/weight-templates";

    /// <summary>GET (Grades.View) → <c>WeightTemplateDto</c>; PUT (<c>SaveWeightTemplateRequest</c>) and DELETE — Grades.ConfigureScales; a weight outside 0-100 is 400 <c>weights_invalid</c>.</summary>
    public const string WeightTemplatePattern = OrgUnitWeightTemplatesPattern + "/{tid:guid}";

    /// <summary>
    /// GET <c>?termId=&amp;page=&amp;pageSize=</c> (<c>GradebookQuery</c> → <c>GradebookDto</c>) — Grades.Manage or Grades.View; Rel; the backfill of events recorded before M5 runs
    /// only for Manage and never in a closed year; student, guardian and counselor 403, another teacher 404.
    /// </summary>
    public const string ClassCourseGradebookPattern = ClassCoursePattern + "/gradebook";

    /// <summary>PUT (<c>SaveGradebookConfigRequest</c> → <c>GradebookConfigDto</c>) — Grades.Manage; Rel; PlacementGuard; 409 <c>gradebook_locked</c>; a scale not visible at the unit is 404.</summary>
    public const string ClassCourseGradebookConfigPattern = ClassCourseGradebookPattern + "/config";

    /// <summary>PUT (<c>SaveGradeCategoriesRequest</c> → <c>GradeCategoryDto[]</c>) — Grades.Manage; Rel; weights outside 0-100 are 400 <c>weights_invalid</c>; supervisor 403.</summary>
    public const string ClassCourseGradebookCategoriesPattern = ClassCourseGradebookPattern + "/categories";

    /// <summary>POST (<c>CreateGradeItemRequest</c> → <c>GradeItemDto</c>) — Grades.Manage; Rel; a manual item.</summary>
    public const string ClassCourseGradebookItemsPattern = ClassCourseGradebookPattern + "/items";

    /// <summary>PUT (<c>UpdateGradeItemRequest</c> → <c>GradeItemDto</c>) and DELETE (manual items only) — Grades.Manage; Rel.</summary>
    public const string ClassCourseGradebookItemPattern = ClassCourseGradebookItemsPattern + "/{iid:guid}";

    /// <summary>
    /// PUT (<c>UpsertEntriesRequest</c> → <c>GradeRowDto</c>) — Grades.Manage; Rel; PlacementGuard; 409 <c>gradebook_locked</c>; a stale <c>Version</c> is 409 <c>concurrency_conflict</c>;
    /// points out of range 422 <c>grade_out_of_range</c>; an override without a reason 422 <c>grade_override_reason_required</c>; event scores are read only.
    /// </summary>
    public const string ClassCourseGradebookEntriesPattern = ClassCourseGradebookPattern + "/entries";

    /// <summary>
    /// POST (<c>PublishItemsRequest</c> → <c>PublishResultDto</c>) — Grades.Publish; Rel; 409 <c>scale_provisional</c> without the acknowledgement; a schedule in the past 400;
    /// student 403, another teacher 404.
    /// </summary>
    public const string ClassCourseGradebookPublishPattern = ClassCourseGradebookItemsPattern + "/publish";

    /// <summary>GET (<c>termId</c> optional → <c>StudentCourseGradesDto</c>) — Grades.Manage; Rel; the same helper as the student's own route, so staff see exactly what the student sees; a student 403.</summary>
    public const string ClassCourseGradebookStudentViewPattern = ClassCourseGradebookPattern + "/students/{sid:guid}/student-view";

    /// <summary>GET → the item's column as <c>.xlsx</c> keyed by student number (cells escaped against formula injection, L9) — Grades.Manage; Rel; audited.</summary>
    public const string ClassCourseGradebookItemExportPattern = ClassCourseGradebookItemPattern + "/export";

    /// <summary>POST (multipart <c>.xlsx</c> → <c>GradeImportPreviewDto</c>) — Grades.Manage; Rel; 400 <c>grade_import_invalid</c>; event-derived scores are never written.</summary>
    public const string ClassCourseGradebookItemImportPreviewPattern = ClassCourseGradebookItemPattern + "/import/preview";

    /// <summary>POST (<c>ApplyGradeImportRequest</c> → <c>GradeImportResultDto</c>) — Grades.Manage; Rel; re-validated against current data; 400 <c>grade_import_expired</c> for an expired or foreign token; audited.</summary>
    public const string ClassCourseGradebookItemImportApplyPattern = ClassCourseGradebookItemPattern + "/import/apply";

    /// <summary>GET <c>?termId=</c> → <c>StudentGradesDto</c> — Grades.ViewOwn; the caller is a student; published items only; guardian and staff 403.</summary>
    public const string MeGrades = "/api/me/grades";

    /// <summary>GET <c>?termId=</c> → <c>StudentGradesDto</c> — Grades.ViewOwn; an active guardian link (another guardian's child is 404); published items only.</summary>
    public const string StudentGradesPattern = StudentPattern + "/grades";

    /// <summary>GET <c>{kind}</c> from <c>GradeReportKinds</c> (<c>GradeReportQuery</c> → <c>GradeReportDto</c>) — Grades.ViewReports; Supervisor their courses, Manager unit scope; teacher and counselor 403.</summary>
    public const string GradeReportPattern = "/api/grade-reports/{kind}";

    /// <summary>GET <c>{kind}</c> (<c>GradeReportQuery</c>) → the report as <c>.xlsx</c> with the "Scale" sheet — Grades.ViewReports; audited.</summary>
    public const string GradeReportExportPattern = GradeReportPattern + "/export";

    /// <summary>GET → <c>TermStructureDto</c> (AcademicYears.View); PUT (<c>TermStructureDto</c>) — AcademicYears.Update, Planned years only, 409 <c>terms_have_dependants</c> (L8).</summary>
    public const string AcademicYearTermStructurePattern = AcademicYearPattern + "/term-structure";

    public static string OrgUnitGradingScales(Guid unitId) => $"{OrgUnit(unitId)}/grading-scales";
    public static string GradingScale(Guid unitId, Guid scaleId) => $"{OrgUnitGradingScales(unitId)}/{scaleId}";
    public static string GradingScaleConfirm(Guid unitId, Guid scaleId) => $"{GradingScale(unitId, scaleId)}/confirm";
    public static string GradingScaleDefault(Guid unitId, Guid scaleId) => $"{GradingScale(unitId, scaleId)}/default";
    public static string OrgUnitGradingScalePresets(Guid unitId) => $"{OrgUnitGradingScales(unitId)}/presets";
    public static string OrgUnitWeightTemplates(Guid unitId) => $"{OrgUnit(unitId)}/weight-templates";
    public static string WeightTemplate(Guid unitId, Guid templateId) => $"{OrgUnitWeightTemplates(unitId)}/{templateId}";

    public static string ClassCourseGradebook(Guid classCourseId) => $"{ClassCourse(classCourseId)}/gradebook";

    public static string ClassCourseGradebook(Guid classCourseId, Guid? termId, int page = 1, int pageSize = Grading.GradingLimits.GridPageSize) =>
        $"{ClassCourseGradebook(classCourseId)}?{(termId is { } t ? $"termId={t}&" : string.Empty)}page={page}&pageSize={pageSize}";

    // The write routes that act on "the gradebook of a term" take an optional ?termId= (the current term of the class when absent), so a client
    // never builds the query by hand. Item routes with an {iid} need none: the item names its gradebook.
    public static string ClassCourseGradebookConfig(Guid classCourseId, Guid? termId = null) => WithTerm($"{ClassCourseGradebook(classCourseId)}/config", termId);
    public static string ClassCourseGradebookCategories(Guid classCourseId, Guid? termId = null) => WithTerm($"{ClassCourseGradebook(classCourseId)}/categories", termId);
    public static string ClassCourseGradebookItems(Guid classCourseId, Guid? termId = null) => WithTerm($"{ClassCourseGradebook(classCourseId)}/items", termId);
    public static string ClassCourseGradebookItem(Guid classCourseId, Guid itemId) => $"{ClassCourseGradebookItems(classCourseId)}/{itemId}";
    public static string ClassCourseGradebookEntries(Guid classCourseId, Guid? termId = null) => WithTerm($"{ClassCourseGradebook(classCourseId)}/entries", termId);
    public static string ClassCourseGradebookPublish(Guid classCourseId) => $"{ClassCourseGradebookItems(classCourseId)}/publish";

    private static string WithTerm(string url, Guid? termId) => termId is { } t ? $"{url}?termId={t}" : url;
    public static string ClassCourseGradebookStudentView(Guid classCourseId, Guid studentId) => $"{ClassCourseGradebook(classCourseId)}/students/{studentId}/student-view";
    public static string ClassCourseGradebookItemExport(Guid classCourseId, Guid itemId) => $"{ClassCourseGradebookItem(classCourseId, itemId)}/export";
    public static string ClassCourseGradebookItemImportPreview(Guid classCourseId, Guid itemId) => $"{ClassCourseGradebookItem(classCourseId, itemId)}/import/preview";
    public static string ClassCourseGradebookItemImportApply(Guid classCourseId, Guid itemId) => $"{ClassCourseGradebookItem(classCourseId, itemId)}/import/apply";

    public static string MyGrades(Guid? termId = null) => termId is { } t ? $"{MeGrades}?termId={t}" : MeGrades;
    public static string StudentGrades(Guid studentId, Guid? termId = null) => termId is { } t ? $"{Student(studentId)}/grades?termId={t}" : $"{Student(studentId)}/grades";
    public static string GradeReport(string kind) => $"/api/grade-reports/{kind}";
    public static string GradeReportExport(string kind) => $"{GradeReport(kind)}/export";
    public static string AcademicYearTermStructure(Guid yearId) => $"{AcademicYear(yearId)}/term-structure";
}
