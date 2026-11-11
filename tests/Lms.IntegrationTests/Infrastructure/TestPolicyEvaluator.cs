using Lms.Domain.Access;
using Lms.Domain.Organizations;
using Lms.Infrastructure.Configuration;

namespace Lms.IntegrationTests.Infrastructure;

/// <summary>In-memory stand-in for the policy evaluator: a policy "on" a unit blocks a permission in its subtree.</summary>
public sealed class TestPolicyEvaluator : IPolicyEvaluator
{
    private readonly List<(string Permission, Guid UnitId, string Path)> _blocks = [];

    public void Block(string permission, OrgUnit unit) => _blocks.Add((permission, unit.Id, unit.Path));

    public Task<Guid?> FindBlockingUnitAsync(string permission, string orgUnitPath, CancellationToken cancellationToken = default)
    {
        var hit = _blocks
            .Where(b => b.Permission == permission && OrgUnitPath.IsWithin(orgUnitPath, b.Path))
            .OrderBy(b => b.Path.Length)
            .Select(b => (Guid?)b.UnitId)
            .FirstOrDefault();
        return Task.FromResult(hit);
    }

    /// <summary>The fake does not model role-blocking policies; tests that need them use the real evaluator.</summary>
    public Task<Guid?> FindBlockingUnitForRoleAsync(Role role, string orgUnitPath, CancellationToken cancellationToken = default) =>
        Task.FromResult<Guid?>(null);
}