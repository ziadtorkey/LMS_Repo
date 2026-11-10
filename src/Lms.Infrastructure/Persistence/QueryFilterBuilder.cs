using System.Linq.Expressions;
using Lms.Domain.Auditing;
using Lms.Domain.Common;
using Lms.Domain.Organizations;

namespace Lms.Infrastructure.Persistence;

/// <summary>
/// Builds one global filter per entity type from the marker interfaces it implements:
/// ISoftDelete → not deleted; ITenantOwned → caller's organization; IOrgUnitScoped → caller's subtree.
/// Platform admins skip the tenant and scope parts (never the soft-delete part). A null organization or
/// null scope matches nothing.
/// </summary>
internal static class QueryFilterBuilder
{
    public static LambdaExpression? Build(Type clrType, LmsDbContext db)
    {
        var e = Expression.Parameter(clrType, "e");
        var ctx = Expression.Property(Expression.Constant(db), nameof(LmsDbContext.CurrentContext));
        var isAdmin = Expression.Property(ctx, nameof(Lms.Domain.Abstractions.ICurrentContext.IsPlatformAdmin));

        Expression? filter = null;

        if (typeof(ISoftDelete).IsAssignableFrom(clrType))
        {
            filter = And(filter, Expression.Not(Expression.Property(e, nameof(ISoftDelete.IsDeleted))));
        }

        Expression? tenantScope = null;

        if (clrType == typeof(Organization))
        {
            // The tenant root: a client sees only itself.
            tenantScope = And(tenantScope, Expression.Equal(
                Expression.Convert(Expression.Property(e, nameof(Entity.Id)), typeof(Guid?)),
                Expression.Property(ctx, nameof(Lms.Domain.Abstractions.ICurrentContext.OrganizationId))));
        }

        if (clrType == typeof(AuditEntry))
        {
            // Not ITenantOwned: OrganizationId is nullable (events outside any client). A client sees only its own entries.
            // EF turns Guid? == Guid? into "a = b OR (a IS NULL AND b IS NULL)", so a caller without a client would
            // match every client-less entry; the explicit not-null check on the caller side prevents that.
            var callerOrg = Expression.Property(ctx, nameof(Lms.Domain.Abstractions.ICurrentContext.OrganizationId));
            tenantScope = And(tenantScope, Expression.AndAlso(
                Expression.NotEqual(callerOrg, Expression.Constant(null, typeof(Guid?))),
                Expression.Equal(Expression.Property(e, nameof(AuditEntry.OrganizationId)), callerOrg)));
        }

        if (typeof(ITenantOwned).IsAssignableFrom(clrType))
        {
            tenantScope = And(tenantScope, Expression.Equal(
                Expression.Convert(Expression.Property(e, nameof(ITenantOwned.OrganizationId)), typeof(Guid?)),
                Expression.Property(ctx, nameof(Lms.Domain.Abstractions.ICurrentContext.OrganizationId))));
        }

        if (typeof(IOrgUnitScoped).IsAssignableFrom(clrType))
        {
            // OrgUnit maps its path to the "Path" column; OrgUnitPath is a computed alias there.
            var pathProperty = clrType == typeof(OrgUnit) ? nameof(OrgUnit.Path) : nameof(IOrgUnitScoped.OrgUnitPath);
            var scopePath = Expression.Property(ctx, nameof(Lms.Domain.Abstractions.ICurrentContext.ScopePath));
            var startsWith = Expression.Call(
                Expression.Property(e, pathProperty),
                typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!,
                scopePath);
            tenantScope = And(tenantScope, Expression.AndAlso(
                Expression.NotEqual(scopePath, Expression.Constant(null, typeof(string))), startsWith));
        }

        if (tenantScope is not null)
        {
            filter = And(filter, Expression.OrElse(isAdmin, tenantScope));
        }

        return filter is null ? null : Expression.Lambda(filter, e);
    }

    private static Expression And(Expression? left, Expression right) =>
        left is null ? right : Expression.AndAlso(left, right);
}