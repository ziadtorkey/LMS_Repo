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

    public List<(Guid Id, UpdateStaffRequest Request)> Updates { get; } = [];
    public List<(Guid Id, SetPersonStatusRequest Request)> StatusChanges { get; } = [];
    public List<Guid> Deleted { get; } = [];
    public List<Guid> Invitations { get; } = [];
    public List<(Guid Id, ShareStaffRequest Request)> Shares { get; } = [];
    public List<(Guid Id, Guid AssignmentId)> RemovedShares { get; } = [];

    /// <summary>When set, the next write (update, status, delete, invitation, share) throws it.</summary>
    public Exception? WriteFailure { get; set; }

    private Task Write(Guid id, Func<StaffDetailDto, StaffDetailDto>? change, Action record)
    {
        if (WriteFailure is not null) return Task.FromException(WriteFailure);
        var i = _staff.FindIndex(s => s.Summary.Id == id);
        if (i < 0) return Task.FromException(new ApiProblemException(404, "person_not_found", null, null));
        record();
        if (change is not null) _staff[i] = change(_staff[i]);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Guid id, UpdateStaffRequest r, CancellationToken ct = default) =>
        Write(id, d => d with
        {
            NationalId = r.NationalId,
            Summary = d.Summary with
            {
                NameAr = r.NameAr, NameEn = r.NameEn, Mobile = r.Mobile, JobTitleAr = r.JobTitleAr, JobTitleEn = r.JobTitleEn,
                HomeOrgUnitId = r.HomeOrgUnitId, ExpiresOn = r.ExpiresOn, PhotoFileId = r.PhotoFileId,
            },
        }, () => Updates.Add((id, r)));

    public Task SetStatusAsync(Guid id, SetPersonStatusRequest r, CancellationToken ct = default) =>
        Write(id, d => d with
        {
            Summary = d.Summary with { Status = r.Status },
            StatusHistory = [new PersonStatusChangeDto(d.Summary.Status, r.Status, r.Reason, DateTime.UtcNow, "المدير", "Admin"), .. d.StatusHistory],
        }, () => StatusChanges.Add((id, r)));

    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        Write(id, null, () => { Deleted.Add(id); _staff.RemoveAll(s => s.Summary.Id == id); });

    public Task ResendInvitationAsync(Guid id, CancellationToken ct = default) =>
        Write(id, null, () => Invitations.Add(id));

    public Task ShareAsync(Guid id, ShareStaffRequest r, CancellationToken ct = default) =>
        Write(id, d => d with
        {
            Summary = d.Summary with
            {
                Roles = [.. d.Summary.Roles, .. r.Targets.Select(t => new StaffRoleDto(Guid.NewGuid(), t.RoleId, "دور", "Role", t.OrgUnitId,
                    "وحدة", "Unit", t.ValidFrom, t.ValidTo, true))],
            },
        }, () => Shares.Add((id, r)));

    public Task RemoveShareAsync(Guid id, Guid assignmentId, CancellationToken ct = default) =>
        Write(id, d => d with { Summary = d.Summary with { Roles = d.Summary.Roles.Where(x => x.AssignmentId != assignmentId).ToList() } },
            () => RemovedShares.Add((id, assignmentId)));

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
        // A person from another unit, visible only through a share (limited view).
        Add("نورة حسن", "Noura Hassan", "noura@x.test", PersonStatuses.Active, [manager with { AssignmentId = Guid.NewGuid(), IsShare = true }], limited: true);
    }

    /// <summary>Adds a staff member and returns its detail.</summary>
    public StaffDetailDto Add(string nameAr, string nameEn, string email, string status, IReadOnlyList<StaffRoleDto> roles,
        DateOnly? expiresOn = null, bool limited = false, bool activated = true, Guid? photoFileId = null)
    {
        var id = Guid.NewGuid();
        var summary = new StaffSummaryDto(id, Guid.NewGuid(), nameAr, nameEn, email, limited ? null : "0500000000", null, null,
            FakeUnits.SchoolId, "المدرسة الرئيسية", "Main School", status, expiresOn, photoFileId, limited, roles);
        var detail = new StaffDetailDto(summary, limited ? null : "1234567890", activated, limited,
            limited ? [] : [new PersonStatusChangeDto(PersonStatuses.Active, status, "Initial", DateTime.UtcNow.AddDays(-1), "المدير", "Admin")]);
        _staff.Add(detail);
        return detail;
    }

    public Task<PagedResult<StaffSummaryDto>> ListAsync(StaffQuery query, CancellationToken ct = default)
    {
        LastQuery = query;
        var rows = _staff.Select(s => s.Summary)
            .Where(s => query.Status is null || s.Status == query.Status)
            .Where(s => query.OrgUnitId is null || s.HomeOrgUnitId == query.OrgUnitId || s.Roles.Any(r => r.OrgUnitId == query.OrgUnitId))
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
