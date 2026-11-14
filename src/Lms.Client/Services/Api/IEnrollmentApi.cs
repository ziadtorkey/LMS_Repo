using Lms.Shared;
using Lms.Shared.Classes;
using Lms.Shared.Enrollment;

namespace Lms.Client.Services.Api;

/// <summary>Placing students in classes, their course enrollments and transfers between classes (FR-ENR-01..05).</summary>
public interface IEnrollmentApi
{
    Task<IReadOnlyList<ClassStudentDto>> ListClassStudentsAsync(Guid classId, CancellationToken ct = default);

    /// <summary>Beyond capacity: Block mode fails with class_full; Warn mode with over_capacity unless ConfirmOverCapacity.</summary>
    Task<AddStudentsResultDto> AddStudentsAsync(Guid classId, AddStudentsToClassRequest request, CancellationToken ct = default);

    /// <summary>Takes the student out of the class and drops the class's enrollments too.</summary>
    Task RemoveFromClassAsync(Guid classId, Guid classStudentId, RemoveFromClassRequest request, CancellationToken ct = default);

    Task<PagedResult<EnrollmentDto>> ListEnrollmentsAsync(EnrollmentQuery query, CancellationToken ct = default);

    /// <summary>Manual enrollment in an optional course.</summary>
    Task EnrollAsync(Guid classCourseId, EnrollStudentsRequest request, CancellationToken ct = default);

    Task DropAsync(Guid enrollmentId, DropEnrollmentRequest request, CancellationToken ct = default);

    Task<TransferPreviewDto> PreviewTransferAsync(TransferStudentRequest request, CancellationToken ct = default);

    /// <summary>Fails with transfer_invalid when the move is not allowed (e.g. a different year).</summary>
    Task TransferAsync(TransferStudentRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<StudentTransferDto>> ListTransfersAsync(Guid studentId, CancellationToken ct = default);

    /// <summary>Enrollment exceptions: the approvers' queue, or the requester's own requests (the server scopes the list).</summary>
    Task<PagedResult<EnrollmentExceptionDto>> ListExceptionsAsync(ExceptionQuery query, CancellationToken ct = default);

    /// <summary>Asks to enroll a student who fails the course restrictions; fails with a duplicate problem while one is pending.</summary>
    Task RequestExceptionAsync(Guid classCourseId, RequestExceptionRequest request, CancellationToken ct = default);

    Task ApproveExceptionAsync(Guid id, DecideExceptionRequest request, CancellationToken ct = default);

    /// <summary>A comment is required.</summary>
    Task RejectExceptionAsync(Guid id, DecideExceptionRequest request, CancellationToken ct = default);

    /// <summary>The requester withdraws a pending request.</summary>
    Task CancelExceptionAsync(Guid id, CancellationToken ct = default);
}

public sealed class EnrollmentApi(ApiHttp http) : IEnrollmentApi
{
    public async Task<IReadOnlyList<ClassStudentDto>> ListClassStudentsAsync(Guid classId, CancellationToken ct = default) =>
        await http.GetAsync<List<ClassStudentDto>>(Routes.ClassStudents(classId), ct);

    public async Task<AddStudentsResultDto> AddStudentsAsync(Guid classId, AddStudentsToClassRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<AddStudentsResultDto>(HttpMethod.Post, Routes.ClassStudents(classId), request, ct))!;

    public Task RemoveFromClassAsync(Guid classId, Guid classStudentId, RemoveFromClassRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.ClassStudentRemove(classId, classStudentId), request, ct);

    public Task<PagedResult<EnrollmentDto>> ListEnrollmentsAsync(EnrollmentQuery q, CancellationToken ct = default)
    {
        var parts = new List<string> { $"page={q.Page}", $"pageSize={q.PageSize}" };
        if (q.StudentId is { } student) parts.Add($"studentId={student}");
        if (q.ClassCourseId is { } classCourse) parts.Add($"classCourseId={classCourse}");
        if (!string.IsNullOrWhiteSpace(q.Status)) parts.Add($"status={Uri.EscapeDataString(q.Status)}");
        return http.GetAsync<PagedResult<EnrollmentDto>>($"{Routes.Enrollments}?{string.Join('&', parts)}", ct);
    }

    public Task EnrollAsync(Guid classCourseId, EnrollStudentsRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.ClassCourseEnrollments(classCourseId), request, ct);

    public Task DropAsync(Guid enrollmentId, DropEnrollmentRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.EnrollmentDrop(enrollmentId), request, ct);

    public async Task<TransferPreviewDto> PreviewTransferAsync(TransferStudentRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<TransferPreviewDto>(HttpMethod.Post, Routes.TransfersPreview, request, ct))!;

    public Task TransferAsync(TransferStudentRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.Transfers, request, ct);

    public async Task<IReadOnlyList<StudentTransferDto>> ListTransfersAsync(Guid studentId, CancellationToken ct = default) =>
        await http.GetAsync<List<StudentTransferDto>>(Routes.StudentTransfers(studentId), ct);

    public Task<PagedResult<EnrollmentExceptionDto>> ListExceptionsAsync(ExceptionQuery q, CancellationToken ct = default)
    {
        var parts = new List<string> { $"page={q.Page}", $"pageSize={q.PageSize}" };
        if (q.OrgUnitId is { } unit) parts.Add($"orgUnitId={unit}");
        if (!string.IsNullOrWhiteSpace(q.Status)) parts.Add($"status={Uri.EscapeDataString(q.Status)}");
        return http.GetAsync<PagedResult<EnrollmentExceptionDto>>($"{Routes.EnrollmentExceptions}?{string.Join('&', parts)}", ct);
    }

    public Task RequestExceptionAsync(Guid classCourseId, RequestExceptionRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.ClassCourseExceptions(classCourseId), request, ct);

    public Task ApproveExceptionAsync(Guid id, DecideExceptionRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.EnrollmentExceptionApprove(id), request, ct);

    public Task RejectExceptionAsync(Guid id, DecideExceptionRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.EnrollmentExceptionReject(id), request, ct);

    public Task CancelExceptionAsync(Guid id, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.EnrollmentExceptionCancel(id), new DecideExceptionRequest(null), ct);
}

/// <summary>In-memory fake for the standalone dev server and bUnit tests; records what the UI sent.</summary>
public sealed class FakeEnrollmentApi : IEnrollmentApi
{
    public static readonly Guid ClassCourseId = Guid.Parse("00000000-0000-0000-0000-0000000000f1");
    public static readonly Guid ClassCourseId2 = Guid.Parse("00000000-0000-0000-0000-0000000000f2");

    private readonly List<ClassStudentDto> _classStudents;
    private readonly List<EnrollmentDto> _enrollments = [];

    public List<(Guid ClassId, AddStudentsToClassRequest Request)> Adds { get; } = [];
    public List<(Guid ClassId, Guid ClassStudentId, RemoveFromClassRequest Request)> Removals { get; } = [];
    public List<(Guid ClassCourseId, EnrollStudentsRequest Request)> Enrolls { get; } = [];
    public List<(Guid Id, DropEnrollmentRequest Request)> Drops { get; } = [];
    public List<TransferStudentRequest> Previews { get; } = [];
    public List<TransferStudentRequest> Transfers { get; } = [];
    public ExceptionQuery? LastExceptionQuery { get; private set; }
    public List<(Guid ClassCourseId, RequestExceptionRequest Request)> ExceptionRequests { get; } = [];
    public List<(Guid Id, string? Comment)> ExceptionApprovals { get; } = [];
    public List<(Guid Id, string? Comment)> ExceptionRejections { get; } = [];
    public List<Guid> ExceptionCancellations { get; } = [];
    private readonly List<EnrollmentExceptionDto> _exceptions = [];

    /// <summary>What the next preview returns.</summary>
    public TransferPreviewDto Preview { get; set; } = new(["MATH1", "SCI1"], ["ART1"], ["ARAB1"], ["FR1"], false);

    /// <summary>What the next add returns (restriction skips, over capacity).</summary>
    public AddStudentsResultDto? AddResult { get; set; }

    /// <summary>When set, the next add / transfer / drop throws it (tests simulate class_full, over_capacity, transfer_invalid).</summary>
    public Exception? Failure { get; set; }

    public FakeEnrollmentApi()
    {
        _classStudents =
        [
            new(Guid.Parse("00000000-0000-0000-0000-0000000000a9"), Guid.Parse("00000000-0000-0000-0000-0000000000c1"), "S-1001", "أحمد سعيد", "Ahmed Saeed",
                Lms.Shared.People.Genders.Male, new DateOnly(2026, 9, 1), null, ClassStudentStatuses.Active),
            new(Guid.Parse("00000000-0000-0000-0000-0000000000aa"), Guid.Parse("00000000-0000-0000-0000-0000000000c2"), "S-1002", "ليلى حسن", "Laila Hassan",
                Lms.Shared.People.Genders.Female, new DateOnly(2026, 9, 1), null, ClassStudentStatuses.Active),
        ];
        _enrollments.AddRange(
        [
            Enrollment("MATH1", "الرياضيات", "Mathematics", EnrollmentSources.Auto, ClassCourseId),
            Enrollment("SCI1", "العلوم", "Science", EnrollmentSources.Auto, ClassCourseId2),
            Enrollment("MUS1", "الموسيقى", "Music", EnrollmentSources.Manual, Guid.NewGuid()),
            Enrollment("ART1", "الفنون", "Art", EnrollmentSources.Exceptional, Guid.NewGuid(), EnrollmentStatuses.Dropped, "Schedule clash"),
            OtherStudent("00000000-0000-0000-0000-0000000000c2", "S-1002", "ليلى حسن", "Laila Hassan"),
            OtherStudent("00000000-0000-0000-0000-0000000000c3", "S-1003", "عمر خالد", "Omar Khaled"),
            OtherStudent("00000000-0000-0000-0000-0000000000c4", "S-1004", "سارة علي", "Sara Ali"),
        ]);
        SeedException("S-1003", "عمر خالد", "Omar Khaled", ExceptionStatuses.Pending, "The student studied the course abroad.", "Gender");
        SeedException("S-1004", "سارة علي", "Sara Ali", ExceptionStatuses.Pending, "Parent request.", "Religion", "Gender");
        SeedException("S-1002", "ليلى حسن", "Laila Hassan", ExceptionStatuses.Rejected, "Wants to join.", "LevelType");
    }

    private static EnrollmentDto Enrollment(string code, string ar, string en, string source, Guid classCourseId,
        string status = EnrollmentStatuses.Active, string? dropReason = null) =>
        new(Guid.NewGuid(), classCourseId, Guid.Parse("00000000-0000-0000-0000-0000000000c1"), "S-1001", "أحمد سعيد", "Ahmed Saeed", code, ar, en,
            "الصف الأول أ", "Grade 1 A", source, status, new DateOnly(2026, 9, 1), status == EnrollmentStatuses.Dropped ? new DateOnly(2026, 10, 1) : null,
            dropReason, null, null, null);

    /// <summary>A classmate enrolled in the second class course (the groups demo and tests).</summary>
    private static EnrollmentDto OtherStudent(string id, string code, string nameAr, string nameEn) =>
        new(Guid.NewGuid(), ClassCourseId2, Guid.Parse(id), code, nameAr, nameEn, "SCI1", "العلوم", "Science", "الصف الأول أ", "Grade 1 A",
            EnrollmentSources.Auto, EnrollmentStatuses.Active, new DateOnly(2026, 9, 1), null, null, null, null, null);

    public IReadOnlyList<EnrollmentDto> Enrollments => _enrollments;
    public IReadOnlyList<EnrollmentExceptionDto> Exceptions => _exceptions;

    /// <summary>Seeds an exception request (tests and the dev server).</summary>
    public EnrollmentExceptionDto SeedException(string studentCode, string nameAr, string nameEn, string status, string reason, params string[] failedTypes)
    {
        var item = new EnrollmentExceptionDto(Guid.NewGuid(), ClassCourseId, Guid.NewGuid(), studentCode, nameAr, nameEn, "الرياضيات", "Mathematics",
            "الصف الأول أ", "Grade 1 A", failedTypes, reason, status, "مدير النظام", "Administrator", DateTime.UtcNow.AddHours(-3),
            status == ExceptionStatuses.Pending ? null : "المشرف", status == ExceptionStatuses.Pending ? null : "Supervisor",
            status == ExceptionStatuses.Pending ? null : DateTime.UtcNow.AddHours(-1), status == ExceptionStatuses.Rejected ? "Not justified." : null);
        _exceptions.Add(item);
        return item;
    }
    public IReadOnlyList<ClassStudentDto> ClassStudents => _classStudents;

    public Task<PagedResult<EnrollmentExceptionDto>> ListExceptionsAsync(ExceptionQuery query, CancellationToken ct = default)
    {
        LastExceptionQuery = query;
        var rows = _exceptions.Where(e => query.Status is null || e.Status == query.Status).OrderByDescending(e => e.CreatedAt).ToList();
        return Task.FromResult(new PagedResult<EnrollmentExceptionDto>(rows.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList(), rows.Count, query.Page, query.PageSize));
    }

    public Task RequestExceptionAsync(Guid classCourseId, RequestExceptionRequest request, CancellationToken ct = default)
    {
        ExceptionRequests.Add((classCourseId, request));
        FailIfSet();
        return Task.CompletedTask;
    }

    private Task Decide(Guid id, string status, string? comment, List<(Guid, string?)> log)
    {
        FailIfSet();
        log.Add((id, comment));
        var index = _exceptions.FindIndex(e => e.Id == id);
        _exceptions[index] = _exceptions[index] with { Status = status, DecisionComment = comment, DecidedAt = DateTime.UtcNow };
        return Task.CompletedTask;
    }

    public Task ApproveExceptionAsync(Guid id, DecideExceptionRequest request, CancellationToken ct = default) =>
        Decide(id, ExceptionStatuses.Approved, request.Comment, ExceptionApprovals);

    public Task RejectExceptionAsync(Guid id, DecideExceptionRequest request, CancellationToken ct = default) =>
        Decide(id, ExceptionStatuses.Rejected, request.Comment, ExceptionRejections);

    public Task CancelExceptionAsync(Guid id, CancellationToken ct = default)
    {
        ExceptionCancellations.Add(id);
        FailIfSet();
        var index = _exceptions.FindIndex(e => e.Id == id);
        if (index >= 0) _exceptions[index] = _exceptions[index] with { Status = ExceptionStatuses.Cancelled };
        return Task.CompletedTask;
    }

    private void FailIfSet()
    {
        if (Failure is not { } failure) return;
        Failure = null;
        throw failure;
    }

    public Task<IReadOnlyList<ClassStudentDto>> ListClassStudentsAsync(Guid classId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ClassStudentDto>>(_classStudents.ToList());

    public Task<AddStudentsResultDto> AddStudentsAsync(Guid classId, AddStudentsToClassRequest request, CancellationToken ct = default)
    {
        Adds.Add((classId, request));
        FailIfSet();
        return Task.FromResult(AddResult ?? new AddStudentsResultDto(request.StudentIds.Count, request.StudentIds.Count * 2, [], false));
    }

    public Task RemoveFromClassAsync(Guid classId, Guid classStudentId, RemoveFromClassRequest request, CancellationToken ct = default)
    {
        Removals.Add((classId, classStudentId, request));
        var index = _classStudents.FindIndex(s => s.Id == classStudentId);
        if (index >= 0) _classStudents[index] = _classStudents[index] with { Status = ClassStudentStatuses.Removed, To = request.EffectiveOn };
        return Task.CompletedTask;
    }

    public Task<PagedResult<EnrollmentDto>> ListEnrollmentsAsync(EnrollmentQuery query, CancellationToken ct = default)
    {
        // The seeded enrollments belong to whichever student is asked about (the fake students have random ids).
        var rows = _enrollments
            .Select(e => query.StudentId is { } student ? e with { StudentId = student } : e)
            .Where(e => query.ClassCourseId is null || e.ClassCourseId == query.ClassCourseId)
            .Where(e => query.Status is null || e.Status == query.Status)
            .ToList();
        return Task.FromResult(new PagedResult<EnrollmentDto>(rows.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList(), rows.Count, query.Page, query.PageSize));
    }

    public Task EnrollAsync(Guid classCourseId, EnrollStudentsRequest request, CancellationToken ct = default)
    {
        Enrolls.Add((classCourseId, request));
        return Task.CompletedTask;
    }

    public Task DropAsync(Guid enrollmentId, DropEnrollmentRequest request, CancellationToken ct = default)
    {
        Drops.Add((enrollmentId, request));
        FailIfSet();
        var index = _enrollments.FindIndex(e => e.Id == enrollmentId);
        if (index >= 0) _enrollments[index] = _enrollments[index] with { Status = EnrollmentStatuses.Dropped, DropReason = request.Reason, To = request.EffectiveOn };
        return Task.CompletedTask;
    }

    public Task<TransferPreviewDto> PreviewTransferAsync(TransferStudentRequest request, CancellationToken ct = default)
    {
        Previews.Add(request);
        FailIfSet();
        return Task.FromResult(Preview);
    }

    public Task TransferAsync(TransferStudentRequest request, CancellationToken ct = default)
    {
        Transfers.Add(request);
        FailIfSet();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StudentTransferDto>> ListTransfersAsync(Guid studentId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<StudentTransferDto>>(
        [
            new(Guid.NewGuid(), studentId, "الصف الأول ب", "Grade 1 B", "الصف الأول أ", "Grade 1 A", new DateOnly(2026, 10, 15),
                "Parent request", "مدير النظام", "Administrator", DateTime.UtcNow.AddDays(-20)),
        ]);
}
