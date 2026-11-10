namespace Lms.Infrastructure.Configuration;

public interface IPolicyEvaluator
{
    /// <summary>The highest ancestor-or-self unit holding a policy that blocks the permission, or null.</summary>
    Task<Guid?> FindBlockingUnitAsync(string permission, string orgUnitPath, CancellationToken cancellationToken = default);
}
