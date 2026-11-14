using Lms.Domain.Content;
using Lms.Domain.Enrollment;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Content;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Content;

/// <summary>Maps module rows to the contract DTOs (the node of the plan tree and the module detail).</summary>
internal static class ModuleMapper
{
    /// <summary>Group names of the given modules, keyed by group id.</summary>
    public static async Task<Dictionary<Guid, StudentGroup>> GroupsAsync(LmsDbContext db, IEnumerable<LessonModule> modules, CancellationToken ct)
    {
        var ids = modules.Where(m => m.GroupId is not null).Select(m => m.GroupId!.Value).Distinct().ToList();
        return ids.Count == 0
            ? []
            : await db.Set<StudentGroup>().Where(g => ids.Contains(g.Id)).ToDictionaryAsync(g => g.Id, ct);
    }

    /// <summary>Values read from MySql carry no kind; the contract says UTC, so the JSON gets its Z.</summary>
    public static DateTime? Utc(DateTime? value) => value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;

    /// <summary><paramref name="states"/> = the student states by module id (a student reader), null for staff.</summary>
    public static ModuleNodeDto Node(
        LessonModule m, DateTime nowUtc, IReadOnlyDictionary<Guid, StudentGroup> groups, IReadOnlyDictionary<Guid, string>? states,
        IReadOnlySet<Guid>? removedRefs = null)
    {
        var group = m.GroupId is { } id && groups.TryGetValue(id, out var g) ? g : null;
        return new ModuleNodeDto(
            m.Id, m.Kind, m.Order, m.TitleAr, m.TitleEn, m.Visibility, Utc(m.VisibleFromUtc), Utc(m.VisibleToUtc), m.Target, m.GroupId,
            group?.NameAr, group?.NameEn, m.CompletionRule, m.IsVisibleNow(nowUtc),
            states is null ? null : states.GetValueOrDefault(m.Id, CompletionStates.NotStarted),
            m.RefId is { } refId && removedRefs?.Contains(refId) == true);
    }

    public static ModuleContentDto Content(LessonModule m, ModuleContent? content, Asset? asset) =>
        new(asset is null ? null : AssetEndpoints.ToDto(asset), content?.BodyHtmlAr, content?.BodyHtmlEn, content?.Url, m.RefId);
}
