using Lms.Domain.Access;
using Lms.Domain.Abstractions;
using Lms.Domain.Messaging;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Authorization;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Messaging.Policy;

/// <summary>
/// Who the caller may start a conversation with, found by name (M6 Task 11, D69, plan site 3). Only users the policy allows are returned, so a student typing a
/// classmate's name or a guardian another guardian's gets exactly the answer of no match: the same shape, with nothing that tells a refusal from an absence. The search needs
/// at least <see cref="MessagingLimits.SearchMinLength"/> characters and is paged; it never leaves the caller's client.
/// </summary>
public sealed class RecipientSearch(LmsDbContext db, IMessagingPolicy policy, IClock clock, RelationshipLookup relationships)
{
    /// <summary>Candidates read per round trip, in name order.</summary>
    private const int Batch = 100;

    /// <summary>The cap on <b>work</b>, never on results: the most distinct people the policy is asked about for one search.</summary>
    private const int MaxEvaluated = 600;

    public async Task<IReadOnlyList<RecipientDto>> SearchAsync(Guid organizationId, Guid callerUserId, string? search, int page, int pageSize, CancellationToken ct)
    {
        var text = search?.Trim();
        if (text is null || text.Length < MessagingLimits.SearchMinLength)
        {
            return [];
        }

        var today = DateOnly.FromDateTime(clock.UtcNow); // the client calendar (M5) replaces this when it is on main
        var size = Math.Clamp(pageSize, 1, MessagingLimits.SearchMaxResults);
        var wanted = Math.Max(page, 1) * size;

        // Pre-filter in SQL by role: only the roles the caller's own roles may reach at all (a student never lists classmates, so 200 of them cannot crowd out a teacher).
        var callerRoles = (await relationships.RolesAsync(organizationId, callerUserId, today, [.. MessagingRuleDefaults.Roles], ct)).Keys.ToList();
        var stored = (await db.Set<MessagingRule>().Where(r => r.OrganizationId == organizationId).Select(r => new { r.FromRole, r.ToRole, r.Mode }).ToListAsync(ct))
            .ToDictionary(r => (r.FromRole, r.ToRole), r => r.Mode);
        var reachableRoles = MessagingRuleDefaults.Roles
            .Where(to => callerRoles.Any(from => MessagingPolicy.Effective(stored, from, to) != MessagingRuleModes.Deny)).ToList();
        if (reachableRoles.Count == 0)
        {
            return [];
        }

        var allowed = new List<RecipientDto>();
        var seen = new HashSet<Guid>();
        for (var skip = 0; allowed.Count < wanted && seen.Count < MaxEvaluated; skip += Batch)
        {
            // ISOLATION-BYPASS: the people a caller may write to are in other units of the client; candidates are users with a valid built-in-role assignment in this client whose
            // display name contains the text. Only the user id, names and role key are read, and a candidate is returned only after the messaging policy allows the caller to start a
            // conversation with them. Client matched, live rows.
            var batch = await (from a in db.Set<RoleAssignment>().IgnoreQueryFilters()
                               join r in db.Set<Role>().IgnoreQueryFilters() on a.RoleId equals r.Id
                               join u in db.Users on a.UserId equals u.Id
                               where !a.IsDeleted && !r.IsDeleted && a.OrganizationId == organizationId && r.OrganizationId == organizationId && r.BuiltInKey != null
                                     && reachableRoles.Contains(r.BuiltInKey) && a.UserId != callerUserId && (a.ValidFrom == null || a.ValidFrom <= today) && (a.ValidTo == null || a.ValidTo >= today)
                                     && u.Status == Lms.Domain.Identity.UserStatus.Active
                                     && (u.DisplayNameAr.Contains(text) || u.DisplayNameEn.Contains(text))
                               select new { u.Id, u.DisplayNameAr, u.DisplayNameEn, RoleKey = r.BuiltInKey! })
                .OrderBy(c => c.DisplayNameEn).ThenBy(c => c.Id).ThenBy(c => c.RoleKey).Skip(skip).Take(Batch).ToListAsync(ct);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (var candidate in batch)
            {
                if (!seen.Add(candidate.Id))
                {
                    continue; // one row per person, whatever the number of roles
                }

                if ((await policy.CanStartAsync(organizationId, callerUserId, candidate.Id, ct)).Allowed)
                {
                    allowed.Add(new RecipientDto(candidate.Id, candidate.DisplayNameAr, candidate.DisplayNameEn, candidate.RoleKey, null, null));
                }
            }
        }

        return [.. allowed.Skip((Math.Max(page, 1) - 1) * size).Take(size)];
    }
}

/// <summary>The caller's rules in words for the new-message dialog: which roles they may reach and whether they may create groups (M6 Task 11, D69).</summary>
public sealed class RulesSummaryBuilder(LmsDbContext db, RelationshipLookup relationships, IClock clock)
{
    private static readonly HashSet<string> GroupCreators =
        [BuiltInRoles.Teacher, BuiltInRoles.Counselor, BuiltInRoles.Supervisor, BuiltInRoles.Manager, BuiltInRoles.OrgAdmin];

    public async Task<MessagingRulesSummaryDto> BuildAsync(Guid organizationId, Guid callerUserId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow); // the client calendar (M5) replaces this when it is on main
        var mine = (await relationships.RolesAsync(organizationId, callerUserId, today, [.. MessagingRuleDefaults.Roles], ct)).Keys.ToList();
        var stored = (await db.Set<MessagingRule>().Where(r => r.OrganizationId == organizationId).Select(r => new { r.FromRole, r.ToRole, r.Mode }).ToListAsync(ct))
            .ToDictionary(r => (r.FromRole, r.ToRole), r => r.Mode);
        var reachable = MessagingRuleDefaults.Roles
            .Where(to => mine.Any(from => MessagingPolicy.Effective(stored, from, to) != MessagingRuleModes.Deny)).ToList();
        return new MessagingRulesSummaryDto(reachable, mine.Any(GroupCreators.Contains));
    }
}
