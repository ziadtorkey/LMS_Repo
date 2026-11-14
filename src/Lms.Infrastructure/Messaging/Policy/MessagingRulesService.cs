using Lms.Domain.Messaging;
using Lms.Infrastructure.Persistence;
using Lms.Shared.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Messaging.Policy;

/// <summary>The outcome of saving the rules: ok, or the cells that are looser than the defaults (400 <c>rules_looser_than_default</c>, naming the cells).</summary>
public sealed record SaveRulesResult(bool Ok, IReadOnlyList<MessagingRuleCellDto> Loosened);

/// <summary>
/// The client's narrowed rules matrix (M6 Task 11, L4): reading returns every cell as the policy will use it (<c>min(stored, default)</c>) next to the defaults, saving accepts only
/// cells that are not looser than their default on <c>Deny &lt; Relationship &lt; SameUnit &lt; Allow</c> and refuses the whole request otherwise. Saving a cell back to its default
/// stores that default (a restore); the policy never reads a stored mode above the default anyway.
/// </summary>
public sealed class MessagingRulesService(LmsDbContext db)
{
    public async Task<MessagingRulesDto> GetAsync(Guid organizationId, CancellationToken ct)
    {
        var stored = await db.Set<MessagingRule>().Where(r => r.OrganizationId == organizationId).ToListAsync(ct);
        var storedByCell = stored.ToDictionary(r => (r.FromRole, r.ToRole), r => r.Mode);
        var cells = MessagingRuleDefaults.Matrix()
            .Select(d => new MessagingRuleCellDto(d.FromRole, d.ToRole, MessagingPolicy.Effective(storedByCell, d.FromRole, d.ToRole))).ToList();
        return new MessagingRulesDto(cells, MessagingRuleDefaults.Matrix());
    }

    public async Task<SaveRulesResult> SaveAsync(Guid organizationId, IReadOnlyList<MessagingRuleCellDto> cells, CancellationToken ct)
    {
        var loosened = MessagingRuleDefaults.Loosened(cells);
        if (loosened.Count > 0 || cells.GroupBy(c => (c.FromRole, c.ToRole)).Any(g => g.Count() > 1))
        {
            return new SaveRulesResult(false, loosened.Count > 0 ? loosened : [.. cells.GroupBy(c => (c.FromRole, c.ToRole)).Where(g => g.Count() > 1).Select(g => g.First())]);
        }

        var existing = await db.Set<MessagingRule>().Where(r => r.OrganizationId == organizationId).ToListAsync(ct);
        foreach (var cell in cells)
        {
            var row = existing.FirstOrDefault(r => r.FromRole == cell.FromRole && r.ToRole == cell.ToRole);
            if (row is null)
            {
                db.Add(new MessagingRule(organizationId, cell.FromRole, cell.ToRole, cell.Mode));
            }
            else
            {
                row.SetMode(cell.Mode);
            }
        }

        await db.SaveChangesAsync(ct);
        return new SaveRulesResult(true, []);
    }
}
