using Lms.Shared;
using Lms.Shared.Enrollment;

namespace Lms.Client.Services.Api;

/// <summary>Student groups of a class course (FR-GRP-01..03); a student is in at most one group per class course.</summary>
public interface IGroupsApi
{
    Task<IReadOnlyList<StudentGroupDto>> ListAsync(Guid classCourseId, CancellationToken ct = default);
    Task<StudentGroupDto> CreateAsync(Guid classCourseId, SaveGroupRequest request, CancellationToken ct = default);
    Task UpdateAsync(Guid groupId, SaveGroupRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid groupId, CancellationToken ct = default);
    Task<IReadOnlyList<GroupMemberDto>> ListMembersAsync(Guid groupId, CancellationToken ct = default);

    /// <summary>Fails with already_in_group when a student is in another group of the class course.</summary>
    Task AddMembersAsync(Guid groupId, GroupMembersRequest request, CancellationToken ct = default);

    Task RemoveMembersAsync(Guid groupId, GroupMembersRequest request, CancellationToken ct = default);

    /// <summary>Ungrouped enrolled students that match the group's MatchType/MatchValue.</summary>
    Task<IReadOnlyList<GroupMemberDto>> SuggestionsAsync(Guid groupId, CancellationToken ct = default);
}

public sealed class GroupsApi(ApiHttp http) : IGroupsApi
{
    public async Task<IReadOnlyList<StudentGroupDto>> ListAsync(Guid classCourseId, CancellationToken ct = default) =>
        await http.GetAsync<List<StudentGroupDto>>(Routes.ClassCourseGroups(classCourseId), ct);

    public async Task<StudentGroupDto> CreateAsync(Guid classCourseId, SaveGroupRequest request, CancellationToken ct = default) =>
        (await http.SendAsync<StudentGroupDto>(HttpMethod.Post, Routes.ClassCourseGroups(classCourseId), request, ct))!;

    public Task UpdateAsync(Guid groupId, SaveGroupRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Put, Routes.Group(groupId), request, ct);

    public Task DeleteAsync(Guid groupId, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Delete, Routes.Group(groupId), null, ct);

    public async Task<IReadOnlyList<GroupMemberDto>> ListMembersAsync(Guid groupId, CancellationToken ct = default) =>
        await http.GetAsync<List<GroupMemberDto>>(Routes.GroupMembers(groupId), ct);

    public Task AddMembersAsync(Guid groupId, GroupMembersRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.GroupMembers(groupId), request, ct);

    public Task RemoveMembersAsync(Guid groupId, GroupMembersRequest request, CancellationToken ct = default) =>
        http.SendAsync(HttpMethod.Post, Routes.GroupMembersRemove(groupId), request, ct);

    public async Task<IReadOnlyList<GroupMemberDto>> SuggestionsAsync(Guid groupId, CancellationToken ct = default) =>
        await http.GetAsync<List<GroupMemberDto>>(Routes.GroupSuggestions(groupId), ct);
}

/// <summary>In-memory fake for the standalone dev server and bUnit tests; keeps the one-group rule so conflicts can be shown.</summary>
public sealed class FakeGroupsApi : IGroupsApi
{
    private readonly Dictionary<Guid, List<StudentGroupDto>> _groups = [];
    private readonly Dictionary<Guid, List<GroupMemberDto>> _members = [];

    public FakeGroupsApi()
    {
        // The second class course of the fake enrollments: Ahmed is already in group A; Laila, Omar and Sara are ungrouped.
        Directory.AddRange(
        [
            new GroupMemberDto(Guid.Parse("00000000-0000-0000-0000-0000000000c1"), "S-1001", "أحمد سعيد", "Ahmed Saeed"),
            new GroupMemberDto(Guid.Parse("00000000-0000-0000-0000-0000000000c2"), "S-1002", "ليلى حسن", "Laila Hassan"),
            new GroupMemberDto(Guid.Parse("00000000-0000-0000-0000-0000000000c3"), "S-1003", "عمر خالد", "Omar Khaled"),
            new GroupMemberDto(Guid.Parse("00000000-0000-0000-0000-0000000000c4"), "S-1004", "سارة علي", "Sara Ali"),
        ]);
        Seed(FakeEnrollmentApi.ClassCourseId2, "المجموعة أ", "Group A", null, null, Directory[0]);
        Suggested.Add(Directory[3]);
    }

    /// <summary>Students that suggestions return for a group with a match (the fake has no student data to match on).</summary>
    public List<GroupMemberDto> Suggested { get; } = [];

    public List<(Guid ClassCourseId, SaveGroupRequest Request)> Created { get; } = [];
    public List<(Guid Id, SaveGroupRequest Request)> Updated { get; } = [];
    public List<Guid> Deleted { get; } = [];
    public List<(Guid GroupId, IReadOnlyList<Guid> StudentIds)> Added { get; } = [];
    public List<(Guid GroupId, IReadOnlyList<Guid> StudentIds)> Removed { get; } = [];

    /// <summary>Seeds a group of a class course with members (tests and the dev server).</summary>
    public StudentGroupDto Seed(Guid classCourseId, string nameAr, string nameEn, string? matchType = null, string? matchValue = null, params GroupMemberDto[] members)
    {
        var group = new StudentGroupDto(Guid.NewGuid(), classCourseId, nameAr, nameEn, matchType, matchValue, members.Length);
        GroupsOf(classCourseId).Add(group);
        _members[group.Id] = [.. members];
        return group;
    }

    private List<StudentGroupDto> GroupsOf(Guid classCourseId) =>
        _groups.TryGetValue(classCourseId, out var list) ? list : _groups[classCourseId] = [];

    public Task<IReadOnlyList<StudentGroupDto>> ListAsync(Guid classCourseId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<StudentGroupDto>>(GroupsOf(classCourseId).Select(g => g with { MemberCount = _members[g.Id].Count }).ToList());

    public Task<StudentGroupDto> CreateAsync(Guid classCourseId, SaveGroupRequest request, CancellationToken ct = default)
    {
        Created.Add((classCourseId, request));
        return Task.FromResult(Seed(classCourseId, request.NameAr, request.NameEn, request.MatchType, request.MatchValue));
    }

    public Task UpdateAsync(Guid groupId, SaveGroupRequest request, CancellationToken ct = default)
    {
        Updated.Add((groupId, request));
        foreach (var list in _groups.Values)
        {
            var index = list.FindIndex(g => g.Id == groupId);
            if (index >= 0) list[index] = list[index] with { NameAr = request.NameAr, NameEn = request.NameEn, MatchType = request.MatchType, MatchValue = request.MatchValue };
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid groupId, CancellationToken ct = default)
    {
        Deleted.Add(groupId);
        foreach (var list in _groups.Values) list.RemoveAll(g => g.Id == groupId);
        _members.Remove(groupId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GroupMemberDto>> ListMembersAsync(Guid groupId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<GroupMemberDto>>(_members.TryGetValue(groupId, out var members) ? members.ToList() : []);

    public Task AddMembersAsync(Guid groupId, GroupMembersRequest request, CancellationToken ct = default)
    {
        Added.Add((groupId, request.StudentIds));
        var classCourse = _groups.First(g => g.Value.Any(x => x.Id == groupId)).Key;
        foreach (var studentId in request.StudentIds)
        {
            // One group per class course (D6): a student of another group of the same class course is a conflict.
            if (GroupsOf(classCourse).Any(g => g.Id != groupId && _members[g.Id].Any(m => m.StudentId == studentId)))
                return Task.FromException(new ApiProblemException(409, "already_in_group", null, null));
        }
        foreach (var studentId in request.StudentIds.Distinct())
        {
            if (_members[groupId].Any(m => m.StudentId == studentId)) continue;
            _members[groupId].Add(Known(studentId));
        }
        return Task.CompletedTask;
    }

    public Task RemoveMembersAsync(Guid groupId, GroupMembersRequest request, CancellationToken ct = default)
    {
        Removed.Add((groupId, request.StudentIds));
        _members[groupId].RemoveAll(m => request.StudentIds.Contains(m.StudentId));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GroupMemberDto>> SuggestionsAsync(Guid groupId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<GroupMemberDto>>(Suggested.ToList());

    /// <summary>Students the fake knows by id (the dialogs pick from enrollments, which carry the names).</summary>
    public List<GroupMemberDto> Directory { get; } = [];

    private GroupMemberDto Known(Guid studentId) =>
        Directory.FirstOrDefault(d => d.StudentId == studentId) ?? new GroupMemberDto(studentId, "S-?", "طالب", "Student");
}
