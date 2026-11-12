using Lms.Domain.Auditing;
using Microsoft.AspNetCore.Identity;

namespace Lms.Domain.Identity;

/// <summary>
/// A person who can sign in. Users are platform-level (not owned by a client); what they may do and where is
/// decided by role assignments. Secrets are marked [AuditIgnore] so they never reach the audit log.
/// </summary>
[Audited]
public sealed class User : IdentityUser<Guid>
{
    public User()
    {
        Id = Guid.CreateVersion7();
    }

    public string DisplayNameAr { get; set; } = string.Empty;
    public string DisplayNameEn { get; set; } = string.Empty;
    public bool IsPlatformAdmin { get; set; }
    public UserStatus Status { get; set; } = UserStatus.Active;

    /// <summary>The password is a temporary default (D24): the user must choose their own before any token is issued.</summary>
    public bool MustChangePassword { get; set; }

    [AuditIgnore]
    public override string? PasswordHash { get; set; }

    [AuditIgnore]
    public override string? SecurityStamp { get; set; }

    [AuditIgnore]
    public override string? ConcurrencyStamp { get; set; }
}
