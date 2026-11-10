using Lms.Domain.Abstractions;

namespace Lms.IntegrationTests.Infrastructure;

public sealed class TestCurrentContext : ICurrentContext
{
    public Guid? UserId { get; set; }
    public Guid? OrganizationId { get; set; }
    public Guid? AssignmentId { get; set; }
    public string? ScopePath { get; set; }
    public bool IsPlatformAdmin { get; set; }

    public static TestCurrentContext PlatformAdmin() => new() { UserId = Guid.NewGuid(), IsPlatformAdmin = true };

    public static TestCurrentContext For(Guid organizationId, string? scopePath) =>
        new() { UserId = Guid.NewGuid(), OrganizationId = organizationId, ScopePath = scopePath };
}