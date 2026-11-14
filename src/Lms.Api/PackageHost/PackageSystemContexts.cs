using Lms.Domain.Abstractions;
using Lms.Infrastructure.Persistence;

namespace Lms.Api.PackageHost;

/// <summary>
/// The system-context sites of the host (plan sites 7 and 8): there is no user and no cookie on the content host, so the work runs
/// under a context built from the <b>signed token</b> alone: its organization and org-unit path, never platform admin. Every global
/// filter then applies, so a token reaches rows of its own client and subtree only.
/// </summary>
public sealed class PackageSystemContexts(IConfiguration configuration)
{
    private sealed class TokenContext(Guid organizationId, string scopePath) : ICurrentContext
    {
        public Guid? UserId => null;
        public Guid? OrganizationId => organizationId;
        public Guid? AssignmentId => null;
        public string? ScopePath => scopePath;
        public bool IsPlatformAdmin => false;
    }

    public LmsDbContext Create(PackageTokenClaims claims) =>
        LmsDbContextFactory.Create(configuration.GetConnectionString("Lms")!, new TokenContext(claims.Org, claims.Unit));
}
