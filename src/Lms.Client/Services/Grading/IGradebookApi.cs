using Lms.Shared.Grading;

namespace Lms.Client.Services.Grading;

/// <summary>
/// The gradebook of a class course, publishing, imports and exports, the student and guardian reads and the grade reports (M5 wave 1,
/// FR-GRD-02..05). The server owns every computed value (final grade, band, category result): the client only shows them.
/// </summary>
public interface IGradebookApi
{
    /// <summary>GET: a page of student rows for a term (<c>null</c> = the current one), items, categories, config and the lock state.</summary>
    Task<GradebookDto> GetAsync(Guid classCourseId, GradebookQuery query, CancellationToken ct = default);

    /// <summary>
    /// PUT config: 409 <c>gradebook_locked</c>. Every write that acts on "the gradebook of a term" (config, categories, new items, entries) takes the term the page
    /// shows: without it the server uses the class's current term, which is not the gradebook on screen when another term is open.
    /// </summary>
    Task<GradebookConfigDto> SaveConfigAsync(Guid classCourseId, SaveGradebookConfigRequest request, Guid? termId = null, CancellationToken ct = default);

    /// <summary>PUT categories: a weight outside 0-100 is 400 <c>weights_invalid</c>.</summary>
    Task<IReadOnlyList<GradeCategoryDto>> SaveCategoriesAsync(Guid classCourseId, SaveGradeCategoriesRequest request, Guid? termId = null, CancellationToken ct = default);

    Task<GradeItemDto> CreateItemAsync(Guid classCourseId, CreateGradeItemRequest request, Guid? termId = null, CancellationToken ct = default);

    Task<GradeItemDto> UpdateItemAsync(Guid classCourseId, Guid itemId, UpdateGradeItemRequest request, CancellationToken ct = default);

    /// <summary>DELETE: manual items only.</summary>
    Task DeleteItemAsync(Guid classCourseId, Guid itemId, CancellationToken ct = default);

    /// <summary>PUT entries: one student's changes with the row <c>Version</c>; 409 <c>concurrency_conflict</c> for a stale one, 422 <c>grade_out_of_range</c>.</summary>
    Task<GradeRowDto> UpsertEntriesAsync(Guid classCourseId, UpsertEntriesRequest request, Guid? termId = null, CancellationToken ct = default);

    /// <summary>POST items/publish: 409 <c>scale_provisional</c> without the acknowledgement.</summary>
    Task<PublishResultDto> PublishAsync(Guid classCourseId, PublishItemsRequest request, CancellationToken ct = default);

    /// <summary>GET students/{id}/student-view: exactly what the student sees (staff preview).</summary>
    Task<StudentCourseGradesDto> GetStudentViewAsync(Guid classCourseId, Guid studentId, Guid? termId = null, CancellationToken ct = default);

    /// <summary>GET items/{id}/export: the column as <c>.xlsx</c>.</summary>
    Task<(Stream Content, string FileName)> ExportItemAsync(Guid classCourseId, Guid itemId, CancellationToken ct = default);

    Task<GradeImportPreviewDto> PreviewImportAsync(Guid classCourseId, Guid itemId, string fileName, Stream content, CancellationToken ct = default);

    Task<GradeImportResultDto> ApplyImportAsync(Guid classCourseId, Guid itemId, ApplyGradeImportRequest request, CancellationToken ct = default);

    /// <summary>GET /api/me/grades: the calling student's published grades.</summary>
    Task<StudentGradesDto> GetMyGradesAsync(Guid? termId = null, CancellationToken ct = default);

    /// <summary>GET /api/students/{id}/grades: a guardian's child (published only).</summary>
    Task<StudentGradesDto> GetStudentGradesAsync(Guid studentId, Guid? termId = null, CancellationToken ct = default);

    Task<GradeReportDto> GetReportAsync(string kind, GradeReportQuery query, CancellationToken ct = default);

    Task<(Stream Content, string FileName)> ExportReportAsync(string kind, GradeReportQuery query, CancellationToken ct = default);
}
