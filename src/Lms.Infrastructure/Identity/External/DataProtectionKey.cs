using Lms.Domain.Common;

namespace Lms.Infrastructure.Identity.External;

/// <summary>
/// An ASP.NET Data Protection key ring entry. Platform-level on purpose: the key ring is shared by every node and every
/// client, so it carries no OrganizationId and no query filter. Contains key material: never exposed through any API.
/// </summary>
public sealed class DataProtectionKey : Entity
{
    public string FriendlyName { get; set; } = string.Empty;

    public string Xml { get; set; } = string.Empty;
}
