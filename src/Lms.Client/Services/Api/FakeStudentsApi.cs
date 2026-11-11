using Lms.Shared;
using Lms.Shared.Classes;
using Lms.Shared.People;

namespace Lms.Client.Services.Api;

/// <summary>In-memory students for the standalone client and bUnit tests; records what the UI sent.</summary>
public sealed class FakeStudentsApi : IStudentsApi
{
    private readonly List<StudentDetailDto> _students = [];
    private int _nextCode = 1000;

    public StudentQuery? LastQuery { get; private set; }
    public CreateStudentRequest? LastCreate { get; private set; }
    public List<CreateStudentRequest> Creates { get; } = [];
    public List<(Guid Id, UpdateStudentRequest Request)> Updates { get; } = [];
    public List<(Guid Id, SetPersonStatusRequest Request)> StatusChanges { get; } = [];
    public List<Guid> Deleted { get; } = [];
    public List<(Guid Id, SetStudentFeatureFlagsRequest Request)> FlagUpdates { get; } = [];
    public List<Guid> PasswordResets { get; } = [];

    /// <summary>Same name and date of birth as an existing student → 409 possible_duplicate until confirmed.</summary>
    public bool DetectDuplicates { get; set; } = true;

    /// <summary>When set, the next create throws it (tests simulate server problems).</summary>
    public Exception? CreateFailure { get; set; }

    public IReadOnlyList<StudentDetailDto> All => _students;

    public FakeStudentsApi()
    {
        Add("أحمد سعيد", "Ahmed Saeed", Genders.Male, new DateOnly(2014, 3, 5), "S-1001", "منى", "Mona", "0501111111", FakeClasses.Grade1A);
        Add("ليلى حسن", "Laila Hassan", Genders.Female, new DateOnly(2013, 9, 12), "S-1002", "منى", "Mona", "0501111111", null);
        Add("عمر خالد", "Omar Khaled", Genders.Male, new DateOnly(2014, 1, 20), "S-1003", null, null, null, null, PersonStatuses.Deactivated);
    }

    public StudentDetailDto Add(string nameAr, string nameEn, string gender, DateOnly dob, string code, string? guardianAr, string? guardianEn,
        string? guardianMobile, ClassSummaryDto? cls, string status = PersonStatuses.Active)
    {
        var summary = new StudentSummaryDto(Guid.NewGuid(), code, nameAr, nameEn, dob, gender, FakeUnits.SchoolId, "المدرسة الرئيسية",
            "Main School", status, null, guardianAr, guardianEn, guardianMobile, cls?.Id, cls?.NameAr, cls?.NameEn);
        var detail = new StudentDetailDto(summary, null, null, Religions.Muslim, "EG", null, null, null, false, false, false,
            new Dictionary<string, bool>(), [], [], 0);
        _students.Add(detail);
        return detail;
    }

    public Task<PagedResult<StudentSummaryDto>> ListAsync(StudentQuery query, CancellationToken ct = default)
    {
        LastQuery = query;
        var rows = _students.Select(s => s.Summary)
            .Where(s => query.OrgUnitId is null || s.HomeOrgUnitId == query.OrgUnitId)
            .Where(s => query.Status is null || s.Status == query.Status)
            .Where(s => query.Gender is null || s.Gender == query.Gender)
            .Where(s => query.ClassId is null || s.CurrentClassId == query.ClassId)
            .Where(s => !query.Unplaced || s.CurrentClassId is null)
            .Where(s => string.IsNullOrWhiteSpace(query.Search)
                || s.NameEn.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                || s.NameAr.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                || s.StudentCode.Contains(query.Search, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return Task.FromResult(new PagedResult<StudentSummaryDto>(
            rows.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList(), rows.Count, query.Page, query.PageSize));
    }

    public Task<StudentDetailDto> GetAsync(Guid id, CancellationToken ct = default) =>
        _students.FirstOrDefault(s => s.Summary.Id == id) is { } found
            ? Task.FromResult(found)
            : Task.FromException<StudentDetailDto>(new ApiProblemException(404, "not_found", null, null));

    public Task<StudentCreatedDto> CreateAsync(CreateStudentRequest request, CancellationToken ct = default)
    {
        LastCreate = request;
        Creates.Add(request);
        if (CreateFailure is { } failure)
        {
            CreateFailure = null;
            return Task.FromException<StudentCreatedDto>(failure);
        }
        var d = request.Data;
        var dups = _students.Count(s => s.Summary.NameEn.Equals(d.NameEn, StringComparison.OrdinalIgnoreCase) && s.Summary.DateOfBirth == d.DateOfBirth);
        if (DetectDuplicates && dups > 0 && !request.ConfirmPossibleDuplicate)
            return Task.FromException<StudentCreatedDto>(new ApiProblemException(409, "possible_duplicate", null, null, count: dups));
        if (request.ClassId is { } classId && FakeClasses.All.FirstOrDefault(c => c.Id == classId) is { Capacity: { } cap } full
            && full.StudentCount >= cap && !request.ConfirmOverCapacity)
            return Task.FromException<StudentCreatedDto>(new ApiProblemException(409, "over_capacity", null, null));
        var code = string.IsNullOrWhiteSpace(request.StudentCode) ? $"S-{++_nextCode}" : request.StudentCode;
        var cls = request.ClassId is { } id ? FakeClasses.All.FirstOrDefault(c => c.Id == id) : null;
        var created = Add(d.NameAr, d.NameEn, d.Gender, d.DateOfBirth, code, null, null, null, cls);
        var password = string.IsNullOrWhiteSpace(request.DefaultPassword) ? "Gen#Pass-4821" : request.DefaultPassword;
        return Task.FromResult(new StudentCreatedDto(created, password, request.Guardians.Count > 0 ? CredentialsDelivery.PrimaryGuardian : CredentialsDelivery.None));
    }

    private void Replace(Guid id, Func<StudentDetailDto, StudentDetailDto> change)
    {
        var index = _students.FindIndex(s => s.Summary.Id == id);
        if (index < 0) throw new ApiProblemException(404, "not_found", null, null);
        _students[index] = change(_students[index]);
    }

    public Task UpdateAsync(Guid id, UpdateStudentRequest request, CancellationToken ct = default)
    {
        Updates.Add((id, request));
        var d = request.Data;
        Replace(id, old => old with
        {
            Summary = old.Summary with
            {
                NameAr = d.NameAr, NameEn = d.NameEn, DateOfBirth = d.DateOfBirth, Gender = d.Gender,
                StudentCode = string.IsNullOrWhiteSpace(request.StudentCode) ? old.Summary.StudentCode : request.StudentCode,
            },
            NicknameAr = d.NicknameAr, NicknameEn = d.NicknameEn, Religion = d.Religion, Nationality = d.Nationality,
            NationalId = d.NationalId, Mobile = d.Mobile, Email = d.Email,
        });
        return Task.CompletedTask;
    }

    public Task SetStatusAsync(Guid id, SetPersonStatusRequest request, CancellationToken ct = default)
    {
        StatusChanges.Add((id, request));
        Replace(id, old => old with
        {
            Summary = old.Summary with { Status = request.Status },
            StatusHistory = [new PersonStatusChangeDto(old.Summary.Status, request.Status, request.Reason, DateTime.UtcNow, null, null), .. old.StatusHistory],
        });
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        Deleted.Add(id);
        _students.RemoveAll(s => s.Summary.Id == id);
        return Task.CompletedTask;
    }

    public Task SetFeatureFlagsAsync(Guid id, SetStudentFeatureFlagsRequest request, CancellationToken ct = default)
    {
        FlagUpdates.Add((id, request));
        Replace(id, old => old with
        {
            FeatureFlagOverrides = request.Overrides.Where(o => o.Value.HasValue).ToDictionary(o => o.Key, o => o.Value!.Value),
        });
        return Task.CompletedTask;
    }

    public Task<StudentCreatedDto> ResetPasswordAsync(Guid id, ResetStudentPasswordRequest request, CancellationToken ct = default)
    {
        PasswordResets.Add(id);
        Replace(id, old => old with { HasAccount = true, MustChangePassword = true });
        var detail = _students.First(s => s.Summary.Id == id);
        var password = string.IsNullOrWhiteSpace(request.DefaultPassword) ? "Reset#Pass-7342" : request.DefaultPassword;
        return Task.FromResult(new StudentCreatedDto(detail, password, CredentialsDelivery.None));
    }
}

public sealed class FakeGuardiansApi : IGuardiansApi
{
    public static readonly Guid MonaId = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    public List<(string? NationalId, string? Mobile)> Lookups { get; } = [];

    /// <summary>Matches the guardian with national id 29001010101 or a mobile ending in 111 (the seeded guardian Mona).</summary>
    public Task<GuardianMatchDto?> MatchAsync(string? nationalId, string? mobile, CancellationToken ct = default)
    {
        Lookups.Add((nationalId, mobile));
        var hit = nationalId == "29001010101" || (mobile?.EndsWith("111", StringComparison.Ordinal) ?? false);
        return Task.FromResult(hit ? new GuardianMatchDto(MonaId, "منى", "Mona", "+*********111", 2) : null);
    }
}

public static class FakeClasses
{
    public static readonly ClassSummaryDto Grade1A = Make("00000000-0000-0000-0000-0000000000d1", "1A", "الصف الأول أ", "Grade 1 A", 30, 12);
    public static readonly ClassSummaryDto Grade1B = Make("00000000-0000-0000-0000-0000000000d2", "1B", "الصف الأول ب", "Grade 1 B", 2, 2);

    public static IReadOnlyList<ClassSummaryDto> All { get; } = [Grade1A, Grade1B];

    private static ClassSummaryDto Make(string id, string code, string ar, string en, int capacity, int students) =>
        new(Guid.Parse(id), code, ar, en, FakeUnits.SchoolId, "المدرسة الرئيسية", "Main School", Guid.Empty, "", "", capacity, students, 0,
            null, null, null, ClassStatuses.Active, false);
}

public sealed class FakeClassesApi : IClassesApi
{
    public ClassQuery? LastQuery { get; private set; }

    public Task<PagedResult<ClassSummaryDto>> ListAsync(ClassQuery query, CancellationToken ct = default)
    {
        LastQuery = query;
        var rows = FakeClasses.All.Where(c => query.OrgUnitId is null || c.OrgUnitId == query.OrgUnitId).ToList();
        return Task.FromResult(new PagedResult<ClassSummaryDto>(rows, rows.Count, query.Page, query.PageSize));
    }
}
