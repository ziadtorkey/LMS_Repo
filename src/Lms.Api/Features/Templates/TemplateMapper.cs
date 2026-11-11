using System.Text.Encodings.Web;
using System.Text.Json;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Branches;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Organizations;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Templates;

internal static class TemplateMapper
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static async Task<IReadOnlyList<OrgTemplateSummaryDto>> ToSummariesAsync(
        LmsDbContext db, IReadOnlyList<OrgTemplate> templates, CancellationToken ct)
    {
        var organizationId = templates.Count > 0 ? templates[0].OrganizationId : Guid.Empty; // one client per call
        var unitIds = templates.Select(t => t.OrgUnitId).Distinct().ToList();

        // ISOLATION-BYPASS: the owner of a Shared template may sit in a sibling subtree outside the caller's scope; only
        // its name is read, by the ids of templates the caller may see, restricted to that client and live units.
        var owners = await db.OrgUnits.IgnoreQueryFilters()
            .Where(u => !u.IsDeleted && u.OrganizationId == organizationId && unitIds.Contains(u.Id))
            .Select(u => new { u.Id, u.NameAr, u.NameEn })
            .ToDictionaryAsync(u => u.Id, ct);

        return templates.Select(t =>
        {
            owners.TryGetValue(t.OrgUnitId, out var owner);
            return new OrgTemplateSummaryDto(
                t.Id, t.NameAr, t.NameEn, t.Visibility.ToString(), t.OrgUnitId, owner?.NameAr ?? string.Empty,
                owner?.NameEn ?? string.Empty, t.UnitCount,
                t.Placeholders.Split(',', StringSplitOptions.RemoveEmptyEntries), t.CreatedAt);
        }).ToList();
    }

    public static async Task<OrgTemplateDetailDto> ToDetailAsync(LmsDbContext db, OrgTemplate template, CancellationToken ct)
    {
        var summary = (await ToSummariesAsync(db, [template], ct))[0];
        var snapshot = JsonSerializer.Deserialize<BranchSnapshot>(template.SnapshotJson, Json)!;
        var nodes = snapshot.Nodes.Select(n => new TemplateNodeDto(
            n.Key, n.ParentKey, n.LevelTypeId, n.NameAr, n.NameEn, n.Code, n.IsLevelTransfer, n.NextTransferKey,
            n.IsCourseManagementLevel, n.PolicyKeys, n.Settings.Count)).ToList();
        return new OrgTemplateDetailDto(summary, nodes);
    }
}
