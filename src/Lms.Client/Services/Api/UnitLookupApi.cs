using Lms.Shared;
using Lms.Shared.Organizations;

namespace Lms.Client.Services.Api;

/// <summary>Read-only list of the units the user can see (unit pickers). GET /api/org-units.</summary>
public interface IUnitLookupApi
{
    Task<IReadOnlyList<OrgUnitNodeDto>> GetUnitsAsync(CancellationToken ct = default);
}

public sealed class UnitLookupApi(ApiHttp http) : IUnitLookupApi
{
    public async Task<IReadOnlyList<OrgUnitNodeDto>> GetUnitsAsync(CancellationToken ct = default) =>
        await http.GetAsync<List<OrgUnitNodeDto>>(Routes.OrgUnits, ct);
}

/// <summary>Fixed sample units shared by the fakes (standalone dev server and tests).</summary>
public static class FakeUnits
{
    public static readonly Guid GroupId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    public static readonly Guid SchoolId = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    public static readonly Guid GradeId = Guid.Parse("00000000-0000-0000-0000-0000000000a3");

    public static IReadOnlyList<OrgUnitNodeDto> All { get; } =
    [
        Node(GroupId, null, "مدارس النور", "Al Noor Schools", "AN", 0),
        Node(SchoolId, GroupId, "المدرسة الرئيسية", "Main School", "MS", 1),
        Node(GradeId, SchoolId, "الصف الأول", "Grade 1", "G1", 2),
    ];

    private static OrgUnitNodeDto Node(Guid id, Guid? parent, string ar, string en, string code, int depth) =>
        new(id, parent, Guid.NewGuid(), ar, en, code, depth, "Active", false, null, false, depth < 2, []);
}

public sealed class FakeUnitLookupApi : IUnitLookupApi
{
    public Task<IReadOnlyList<OrgUnitNodeDto>> GetUnitsAsync(CancellationToken ct = default) =>
        Task.FromResult(FakeUnits.All);
}
