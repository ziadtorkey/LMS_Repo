using Lms.Shared;
using Lms.Shared.People;

namespace Lms.Client.Services.Api;

/// <summary>The signed-in student's own profile and change requests (FR-STU-04).</summary>
public interface IMyProfileApi
{
    Task<MyStudentProfileDto> GetAsync(CancellationToken ct = default);

    /// <summary>Saves non-critical fields directly (field name to string value); a critical field fails with critical_field.</summary>
    Task<MyStudentProfileDto> UpdateAsync(UpdateMyStudentProfileRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<StudentChangeRequestDto>> ListRequestsAsync(CancellationToken ct = default);

    /// <summary>Asks for approval of critical field changes; fails with change_request_pending while one is open.</summary>
    Task<StudentChangeRequestDto> CreateRequestAsync(CreateChangeRequestRequest request, CancellationToken ct = default);

    Task CancelRequestAsync(Guid id, CancellationToken ct = default);
}

public sealed class MyProfileApi(ApiHttp http) : IMyProfileApi
{
    public Task<MyStudentProfileDto> GetAsync(CancellationToken ct = default) =>
        http.GetAsync<MyStudentProfileDto>(Routes.MyStudentProfile, ct);

    public async Task<MyStudentProfileDto> UpdateAsync(UpdateMyStudentProfileRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<MyStudentProfileDto>(HttpMethod.Put, Routes.MyStudentProfile, request, ct))!;

    public async Task<IReadOnlyList<StudentChangeRequestDto>> ListRequestsAsync(CancellationToken ct = default) =>
        await http.GetAsync<List<StudentChangeRequestDto>>(Routes.MyChangeRequests, ct);

    public async Task<StudentChangeRequestDto> CreateRequestAsync(CreateChangeRequestRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<StudentChangeRequestDto>(HttpMethod.Post, Routes.MyChangeRequests, request, ct))!;

    public Task CancelRequestAsync(Guid id, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.MyChangeRequest(id), null, ct);
}

/// <summary>In-memory fake for the standalone dev server and bUnit tests; records what the UI sent.</summary>
public sealed class FakeMyProfileApi : IMyProfileApi
{
    private readonly List<StudentChangeRequestDto> _requests = [];
    private MyStudentProfileDto _profile;

    public List<UpdateMyStudentProfileRequest> Updates { get; } = [];
    public List<CreateChangeRequestRequest> Requests { get; } = [];
    public List<Guid> Cancelled { get; } = [];

    public FakeMyProfileApi()
    {
        var summary = new StudentSummaryDto(Guid.Parse("00000000-0000-0000-0000-0000000000c1"), "S-1001", "أحمد سعيد", "Ahmed Saeed",
            new DateOnly(2014, 3, 5), Genders.Male, FakeUnits.SchoolId, "المدرسة الرئيسية", "Main School", PersonStatuses.Active, null,
            null, null, null, FakeClasses.Grade1A.Id, FakeClasses.Grade1A.NameAr, FakeClasses.Grade1A.NameEn);
        _profile = new MyStudentProfileDto(
            new StudentDetailDto(summary, "أحمودي", "Ahmoudy", Religions.Muslim, "EG", "30001010101", "0501234567", null, true, true, false,
                new Dictionary<string, bool>(), [], [], 0),
            [StudentFields.NicknameAr, StudentFields.NicknameEn, StudentFields.Mobile, StudentFields.Photo],
            StudentFields.DefaultCritical);
    }

    /// <summary>Seeds a request (tests that start with one pending).</summary>
    public StudentChangeRequestDto Seed(string status, params FieldChangeDto[] changes)
    {
        var s = _profile.Student.Summary;
        var request = new StudentChangeRequestDto(Guid.NewGuid(), s.Id, s.StudentCode, s.NameAr, s.NameEn, s.HomeOrgUnitNameAr, s.HomeOrgUnitNameEn,
            status, changes, null, DateTime.UtcNow, null, null, status == ChangeRequestStatuses.Pending ? null : DateTime.UtcNow,
            status == ChangeRequestStatuses.Rejected ? "Name does not match the record." : null);
        _requests.Add(request);
        return request;
    }

    public Task<MyStudentProfileDto> GetAsync(CancellationToken ct = default) => Task.FromResult(_profile);

    public Task<MyStudentProfileDto> UpdateAsync(UpdateMyStudentProfileRequest request, CancellationToken ct = default)
    {
        Updates.Add(request);
        var student = _profile.Student;
        foreach (var (field, value) in request.Changes)
        {
            if (!_profile.EditableFields.Contains(field))
                return Task.FromException<MyStudentProfileDto>(new ApiProblemException(400, "critical_field", null, null, field));
            student = field switch
            {
                StudentFields.NicknameAr => student with { NicknameAr = value },
                StudentFields.NicknameEn => student with { NicknameEn = value },
                StudentFields.Mobile => student with { Mobile = value },
                _ => student,
            };
        }
        _profile = _profile with { Student = student };
        return Task.FromResult(_profile);
    }

    public Task<IReadOnlyList<StudentChangeRequestDto>> ListRequestsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<StudentChangeRequestDto>>(_requests.OrderByDescending(r => r.CreatedAt).ToList());

    public Task<StudentChangeRequestDto> CreateRequestAsync(CreateChangeRequestRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);
        if (_requests.Any(r => r.Status == ChangeRequestStatuses.Pending))
            return Task.FromException<StudentChangeRequestDto>(new ApiProblemException(409, "change_request_pending", null, null));
        var changes = request.Changes.Select(c => new FieldChangeDto(c.Key, Lms.Client.Components.People.StudentFieldText.Current(_profile.Student, c.Key), c.Value)).ToArray();
        var created = Seed(ChangeRequestStatuses.Pending, changes) with { Note = request.Note };
        _requests[^1] = created;
        return Task.FromResult(created);
    }

    public Task CancelRequestAsync(Guid id, CancellationToken ct = default)
    {
        Cancelled.Add(id);
        var index = _requests.FindIndex(r => r.Id == id);
        if (index >= 0) _requests[index] = _requests[index] with { Status = ChangeRequestStatuses.Cancelled };
        return Task.CompletedTask;
    }
}
