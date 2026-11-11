using Lms.Api.Features.Assignments;
using Lms.Api.Infrastructure;
using Lms.Domain.Abstractions;
using Lms.Domain.Access;
using Lms.Domain.Organizations;
using Lms.Domain.People;
using Lms.Infrastructure.Persistence;
using Lms.Shared;
using Lms.Shared.Authorization;
using Lms.Shared.People;
using Microsoft.EntityFrameworkCore;

namespace Lms.Api.Features.Staff;

/// <summary>Reads of the staff endpoints: the list, the profile and the shared mapping to DTOs.</summary>
public sealed class StaffQueries(LmsDbContext db, ICurrentContext context)
{
    // Entities are selected whole: DateOnly columns projected out of joins do not materialize with the MySQL provider.
    private sealed record RoleRow(RoleAssignment A, string RoleNameAr, string RoleNameEn, string UnitNameAr, string UnitNameEn);

    public async Task<IResult> ListAsync(StaffQuery query, HttpContext http, CancellationToken ct)
    {
        var page = query.Page;
        var pageSize = query.PageSize;
        if (page < 1 || pageSize < 1 || pageSize > Paging.MaxPageSize)
        {
            return AssignmentQueries.Invalid(page < 1 ? nameof(query.Page) : nameof(query.PageSize), "paging_invalid");
        }

        if ((query.Status is not null && !PersonStatuses.All.Contains(query.Status))
            || (query.Sort is not null && !StaffSorts.All.Contains(query.Sort)))
        {
            return AssignmentQueries.Invalid(query.Status is not null && !PersonStatuses.All.Contains(query.Status) ? nameof(query.Status) : nameof(query.Sort), "invalid_value");
        }

        var scope = await ScopeUnitAsync(query.OrgUnitId, http, ct);
        if (scope.Problem is not null)
        {
            return scope.Problem;
        }

        var unit = scope.Unit!;
        var organizationId = unit.OrganizationId;
        var roleUsers = StaffRoleUsers(query.RoleId);

        // ISOLATION-BYPASS: the home unit of a shared-in person lies outside the caller's subtree, so the profile filter
        // would hide them. Rows are limited to this client, live profiles, and either a home in the requested unit range
        // (inside the caller's scope: CheckUnitAsync) or a live Staff assignment of the person inside that range.
        var profiles = db.Set<StaffProfile>().IgnoreQueryFilters()
            .Where(p => p.OrganizationId == organizationId && !p.IsDeleted);
        var sharedUsers = db.RoleAssignments
            .Where(a => query.IncludeDescendants ? a.OrgUnitPath.StartsWith(unit.Path) : a.OrgUnitId == unit.Id)
            .Join(db.Roles.Where(r => r.BaseKind == RoleBaseKind.Staff), a => a.RoleId, r => r.Id, (a, _) => a.UserId);
        profiles = query.IncludeDescendants
            ? profiles.Where(p => p.OrgUnitPath.StartsWith(unit.Path) || (query.IncludeShared && sharedUsers.Contains(p.UserId)))
            : profiles.Where(p => p.OrgUnitId == unit.Id || (query.IncludeShared && sharedUsers.Contains(p.UserId)));
        if (roleUsers is not null)
        {
            profiles = profiles.Where(p => roleUsers.Contains(p.UserId));
        }

        if (query.Status is not null)
        {
            var status = Enum.Parse<PersonStatus>(query.Status);
            profiles = profiles.Where(p => p.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            profiles = ApplySearch(profiles, query.Search);
        }

        var ordered = query.Sort switch
        {
            StaffSorts.NameDesc => profiles.OrderByDescending(p => p.NameEnKey).ThenBy(p => p.Id),
            StaffSorts.ExpiresOn => profiles.OrderBy(p => p.ExpiresOn == null).ThenBy(p => p.ExpiresOn).ThenBy(p => p.Id),
            StaffSorts.ExpiresOnDesc => profiles.OrderByDescending(p => p.ExpiresOn).ThenBy(p => p.Id),
            _ => profiles.OrderBy(p => p.NameEnKey).ThenBy(p => p.Id),
        };

        var total = await ordered.CountAsync(ct);
        var rows = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = await MapAsync(rows, unit, query.IncludeDescendants, ct);
        return Results.Ok(new PagedResult<StaffSummaryDto>(items, total, page, pageSize));
    }

    public async Task<IResult> GetAsync(Guid id, CancellationToken ct, string? createdAt = null)
    {
        var organizationId = context.OrganizationId;
        // ISOLATION-BYPASS: a person shared into the caller's scope has a home outside it. The profile is returned only
        // when it is in this client, live, and either homed in scope (full view) or reachable through an in-scope Staff
        // assignment (limited view).
        var profile = organizationId is null ? null : await db.Set<StaffProfile>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == id && p.OrganizationId == organizationId && !p.IsDeleted, ct);
        if (profile is null)
        {
            return ApiProblems.NotFound();
        }

        var homeInScope = context.IsPlatformAdmin || (context.ScopePath is { } scope && profile.OrgUnitPath.StartsWith(scope, StringComparison.Ordinal));
        var roles = await RolesAsync([profile.UserId], ct);
        if (!homeInScope && !roles.ContainsKey(profile.UserId))
        {
            return ApiProblems.NotFound();
        }

        var summary = (await MapAsync([profile], null, false, ct, roles, limited: !homeInScope)).Single();
        var user = await db.Users.FirstAsync(u => u.Id == profile.UserId, ct);
        var history = homeInScope ? await HistoryAsync(profile, ct) : [];
        var dto = new StaffDetailDto(summary, homeInScope ? profile.NationalId : null, user.PasswordHash is not null, !homeInScope, history);
        return createdAt is null ? Results.Ok(dto) : Results.Created(createdAt, dto);
    }

    private async Task<UnitAccessResult> ScopeUnitAsync(Guid? orgUnitId, HttpContext http, CancellationToken ct)
    {
        if (orgUnitId is { } given)
        {
            return await http.CheckUnitAsync(Permissions.Staff.View, given, ct);
        }

        var scopePath = context.ScopePath;
        var unit = scopePath is null ? null : await db.OrgUnits.FirstOrDefaultAsync(u => u.Path == scopePath, ct);
        return unit is null
            ? new UnitAccessResult(null, ApiProblems.NotFound())
            : await http.CheckUnitAsync(Permissions.Staff.View, unit.Id, ct);
    }

    private static bool Home(StaffProfile p, OrgUnit unit, bool descendants) =>
        descendants ? p.OrgUnitPath.StartsWith(unit.Path, StringComparison.Ordinal) : p.OrgUnitId == unit.Id;

    private IQueryable<Guid>? StaffRoleUsers(Guid? roleId) => roleId is null
        ? null
        : db.RoleAssignments.Where(a => a.RoleId == roleId).Select(a => a.UserId);

    private static IQueryable<StaffProfile> ApplySearch(IQueryable<StaffProfile> profiles, string search)
    {
        var text = search.Trim();
        var emailPrefix = text.ToLowerInvariant();
        var mobile = PersonNormalizer.Mobile(text);
        var nationalId = PersonNormalizer.NationalId(text);
        var key = PersonNormalizer.NameKey(text);
        return profiles.Where(p =>
            p.Email.StartsWith(emailPrefix)
            || (mobile != null && p.Mobile == mobile)
            || (nationalId != null && p.NationalId == nationalId)
            || (key != string.Empty && (p.NameArKey.Contains(key) || p.NameEnKey.Contains(key))));
    }

    private async Task<Dictionary<Guid, List<RoleRow>>> RolesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        // The assignment filter limits these to the caller's scope: other units' assignments are not revealed.
        var rows = await (
            from a in db.RoleAssignments
            where userIds.Contains(a.UserId)
            join r in db.Roles on a.RoleId equals r.Id
            join n in db.OrgUnits on a.OrgUnitId equals n.Id
            where r.BaseKind == RoleBaseKind.Staff
            orderby a.OrgUnitPath, r.NameEn, a.Id
            select new RoleRow(a, r.NameAr, r.NameEn, n.NameAr, n.NameEn)).ToListAsync(ct);
        return rows.GroupBy(x => x.A.UserId).ToDictionary(g => g.Key, g => g.ToList());
    }

    private async Task<IReadOnlyList<StaffSummaryDto>> MapAsync(
        IReadOnlyList<StaffProfile> profiles, OrgUnit? range, bool descendants, CancellationToken ct,
        Dictionary<Guid, List<RoleRow>>? preloadedRoles = null, bool? limited = null)
    {
        var roles = preloadedRoles ?? await RolesAsync(profiles.Select(p => p.UserId).ToList(), ct);
        var homeIds = profiles.Select(p => p.OrgUnitId).Distinct().ToList();
        var homes = await db.OrgUnits.Where(u => homeIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        return profiles.Select(p =>
        {
            var sharedIn = limited ?? (range is not null && !Home(p, range, descendants));
            var visibleHome = homes.GetValueOrDefault(p.OrgUnitId);
            var roleDtos = roles.GetValueOrDefault(p.UserId, []).Select(r => new StaffRoleDto(
                r.A.Id, r.A.RoleId, r.RoleNameAr, r.RoleNameEn, r.A.OrgUnitId, r.UnitNameAr, r.UnitNameEn,
                r.A.ValidFrom, r.A.ValidTo, !r.A.OrgUnitPath.StartsWith(p.OrgUnitPath, StringComparison.Ordinal))).ToList();
            // A person shared into the caller's scope is shown by name, email, job title, photo and role only (D29).
            return new StaffSummaryDto(
                p.Id, p.UserId, p.NameAr, p.NameEn, p.Email, sharedIn ? null : p.Mobile, p.JobTitleAr, p.JobTitleEn,
                sharedIn ? Guid.Empty : p.OrgUnitId, sharedIn ? string.Empty : visibleHome?.NameAr ?? string.Empty,
                sharedIn ? string.Empty : visibleHome?.NameEn ?? string.Empty,
                p.Status.ToString(), sharedIn ? null : p.ExpiresOn, p.PhotoFileId, sharedIn, roleDtos);
        }).ToList();
    }

    private async Task<IReadOnlyList<PersonStatusChangeDto>> HistoryAsync(StaffProfile profile, CancellationToken ct)
    {
        var changes = await db.Set<PersonStatusChange>()
            .Where(c => c.Kind == PersonKind.Staff && c.ProfileId == profile.Id)
            .OrderByDescending(c => c.ChangedAt).ToListAsync(ct);
        var userIds = changes.Where(c => c.ChangedBy is not null).Select(c => c.ChangedBy!.Value).Distinct().ToList();
        var names = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        return changes.Select(c =>
        {
            var by = c.ChangedBy is { } id ? names.GetValueOrDefault(id) : null;
            return new PersonStatusChangeDto(
                c.From.ToString(), c.To.ToString(), c.Reason, c.ChangedAt, by?.DisplayNameAr, by?.DisplayNameEn);
        }).ToList();
    }
}
