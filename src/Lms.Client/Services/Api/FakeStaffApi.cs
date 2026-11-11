using Lms.Shared;
using Lms.Shared.People;

namespace Lms.Client.Services.Api;

/// <summary>In-memory staff API for the standalone dev server and bUnit tests (until the backend is used).</summary>
public sealed class FakeStaffApi : IStaffApi
{
    private readonly List<StaffDetailDto> _staff = [];

    /// <summary>The query of the most recent list call (tests assert the filters sent).</summary>
    public StaffQuery? LastQuery { get; private set; }
    public CreateStaffRequest? LastCreate { get; private set; }

    /// <summary>When set, the next create throws it (tests simulate server problems).</summary>
    public Exception? CreateFailure { get; set; }

    public IReadOnlyList<StaffDetailDto> All => _staff;

    public FakeStaffApi()
    {
        var manager = new StaffRoleDto(Guid.NewGuid(), Guid.NewGuid(), "مدير", "Manager", FakeUnits.SchoolId,
            "المدرسة الرئيسية", "Main School", null, null, false);
        Add("سارة أحمد", "Sara Ahmed", "sara@x.test", PersonStatuses.Active, [manager]);
        Add("خالد علي", "Khaled Ali", "khaled@x.test", PersonStatuses.Deactivated,
        [
            manager with { AssignmentId = Guid.NewGuid(), RoleNameAr = "معلم", RoleNameEn = "Teacher" },
            manager with { AssignmentId = Guid.NewGuid(), OrgUnitId = FakeUnits.GroupId, OrgUnitNameAr = "مدارس النور", OrgUnitNameEn = "Al Noor Schools", IsShare = true },
        ]);
    }

    /// <summary>Adds a staff member and returns its detail.</summary>
    public StaffDetailDto Add(string nameAr, string nameEn, string email, string status, IReadOnlyList<StaffRoleDto> roles,
        DateOnly? expiresOn = null, bool limited = false)
    {
        var id = Guid.NewGuid();
        var summary = new StaffSummaryDto(id, Guid.NewGuid(), nameAr, nameEn, email, limited ? null : "0500000000", null, null,
            FakeUnits.SchoolId, "المدرسة الرئيسية", "Main School", status, expiresOn, null, limited, roles);
        var detail = new StaffDetailDto(summary, limited ? null : "1234567890", true, limited,
            limited ? [] : [new PersonStatusChangeDto(PersonStatuses.Active, status, "Initial", DateTime.UtcNow.AddDays(-1), "المدير", "Admin")]);
        _staff.Add(detail);
        return detail;
    }

    public Task<PagedResult<StaffSummaryDto>> ListAsync(StaffQuery query, CancellationToken ct = default)
    {
        LastQuery = query;
        var rows = _staff.Select(s => s.Summary)
            .Where(s => query.Status is null || s.Status == query.Status)
            .Where(s => query.RoleId is null || s.Roles.Any(r => r.RoleId == query.RoleId))
            .Where(s => string.IsNullOrWhiteSpace(query.Search)
                || s.NameEn.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                || s.NameAr.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                || s.Email.Contains(query.Search, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return Task.FromResult(new PagedResult<StaffSummaryDto>(
            rows.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList(), rows.Count, query.Page, query.PageSize));
    }

    public Task<StaffDetailDto> GetAsync(Guid id, CancellationToken ct = default) =>
        _staff.FirstOrDefault(s => s.Summary.Id == id) is { } found
            ? Task.FromResult(found)
            : Task.FromException<StaffDetailDto>(new ApiProblemException(404, "person_not_found", null, null));

    public Task<StaffDetailDto> CreateAsync(CreateStaffRequest request, CancellationToken ct = default)
    {
        if (CreateFailure is not null) return Task.FromException<StaffDetailDto>(CreateFailure);
        LastCreate = request;
        if (_staff.Any(s => string.Equals(s.Summary.Email, request.Email, StringComparison.OrdinalIgnoreCase)))
            return Task.FromException<StaffDetailDto>(new ApiProblemException(409, "duplicate_person", null, null, "email"));
        var roles = request.Roles
            .Select(r => new StaffRoleDto(Guid.NewGuid(), r.RoleId, "", "", r.OrgUnitId, "", "", r.ValidFrom, r.ValidTo, false))
            .ToList();
        return Task.FromResult(Add(request.NameAr, request.NameEn, request.Email, PersonStatuses.Active, roles, request.ExpiresOn));
    }
}
