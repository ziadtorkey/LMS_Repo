using Lms.Domain.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lms.Api.Features.Account;

public sealed class InvitationTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public InvitationTokenProviderOptions()
    {
        // A different name gives a different data-protection purpose, so a reset token is never valid as an
        // invitation token and the other way round.
        Name = "InvitationTokenProvider";
        TokenLifespan = TimeSpan.FromHours(72);
    }
}

/// <summary>Token that lets an invited user choose a first password; valid 72 hours, single use (the security stamp rotates).</summary>
public sealed class InvitationTokenProvider(
    IDataProtectionProvider dataProtection,
    IOptions<InvitationTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<User>> logger)
    : DataProtectorTokenProvider<User>(dataProtection, options, logger)
{
    public const string ProviderName = "Invitation";
    public const string Purpose = "Invitation";
}
