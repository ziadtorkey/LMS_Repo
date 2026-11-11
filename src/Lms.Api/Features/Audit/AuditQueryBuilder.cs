using Lms.Domain.Abstractions;
using Lms.Domain.Auditing;
using Lms.Shared;
using Lms.Shared.Auditing;

namespace Lms.Api.Features.Audit;

/// <summary>
/// Applies visibility (client + subtree) and the viewer filters to the audit log. Visibility rules: a Platform Admin
/// sees everything (optionally narrowed to one client); everybody else sees their own client's entries whose unit lies
/// inside their scope, plus entries without a unit when their scope is the client root (D20).
/// </summary>
public static class AuditQueryBuilder
{
    public static IQueryable<AuditEntry> Build(IQueryable<AuditEntry> source, AuditQuery query, ICurrentContext context)
    {
        var result = Visible(source, query, context);

        if (query.UserId is { } userId)
        {
            result = result.Where(x => x.UserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            var entityType = query.EntityType;
            result = result.Where(x => x.EntityType == entityType);
        }

        if (query.EntityId is { } entityId)
        {
            result = result.Where(x => x.EntityId == entityId);
        }

        if (!string.IsNullOrWhiteSpace(query.Action) && Enum.TryParse<AuditAction>(query.Action, ignoreCase: true, out var action))
        {
            result = result.Where(x => x.Action == action);
        }

        if (query.FromUtc is { } from)
        {
            result = result.Where(x => x.TimestampUtc >= from);
        }

        if (query.ToUtc is { } to)
        {
            result = result.Where(x => x.TimestampUtc <= to);
        }

        return result.OrderByDescending(x => x.TimestampUtc).ThenByDescending(x => x.Id);
    }

    /// <summary>Only the visibility rules (used by the detail endpoint).</summary>
    public static IQueryable<AuditEntry> Visible(IQueryable<AuditEntry> source, AuditQuery query, ICurrentContext context)
    {
        if (context.IsPlatformAdmin)
        {
            // OrganizationId is honoured for the platform admin only.
            return query.OrganizationId is { } org ? source.Where(x => x.OrganizationId == org) : source;
        }

        if (context.OrganizationId is not { } organizationId
            || context.ScopePath is not { Length: > 0 } scope || scope[0] != '/' || scope[^1] != '/')
        {
            return source.Where(_ => false);
        }

        // Client root = a path with a single segment ("/{id}/"); only then do unit-less entries belong to the viewer.
        var isRoot = scope.IndexOf('/', 1) == scope.Length - 1;
        return isRoot
            ? source.Where(x => x.OrganizationId == organizationId && (x.OrgUnitPath == null || x.OrgUnitPath.StartsWith(scope)))
            : source.Where(x => x.OrganizationId == organizationId && x.OrgUnitPath != null && x.OrgUnitPath.StartsWith(scope));
    }

    public static (int Page, int PageSize) NormalizePaging(AuditQuery query) =>
        (Math.Max(query.Page, 1), Math.Clamp(query.PageSize, 1, Paging.MaxPageSize));
}
