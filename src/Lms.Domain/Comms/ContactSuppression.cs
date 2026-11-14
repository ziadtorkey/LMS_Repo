using System.Security.Cryptography;
using System.Text;
using Lms.Domain.Common;

namespace Lms.Domain.Comms;

/// <summary>An address a channel must never be tried on again (hard bounce, complaint, opt-out). Only the SHA-256 of the normalised address is kept, never the address.</summary>
public sealed class ContactSuppression : Entity, ITenantOwned
{
    private ContactSuppression()
    {
        Channel = Reason = AddressHash = string.Empty;
    }

    public ContactSuppression(Guid organizationId, string channel, string address, string reason, DateTime now)
    {
        OrganizationId = organizationId;
        Channel = channel;
        AddressHash = Hash(address);
        Reason = reason;
        CreatedAtUtc = now;
    }

    public Guid OrganizationId { get; private set; }
    public string Channel { get; private set; }
    public string AddressHash { get; private set; }
    public string Reason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static string Hash(string address) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(address.Trim().ToLowerInvariant())));
}

public static class SuppressionReasons
{
    public const string HardBounce = "HardBounce";
    public const string Complaint = "Complaint";
    public const string UserOptOut = "UserOptOut";
    public const string InvalidAddress = "InvalidAddress";
}
