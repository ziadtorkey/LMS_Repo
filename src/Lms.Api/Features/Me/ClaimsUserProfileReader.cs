using Lms.Domain.Identity;

namespace Lms.Api.Features.Me;

/// <summary>
/// Interim reader: both display names come from the token's "name" claim until the identity module provides a
/// user-store-backed <see cref="IUserProfileReader"/> with separate Arabic and English names.
/// </summary>
public sealed class ClaimsUserProfileReader(IHttpContextAccessor accessor) : IUserProfileReader
{
    public Task<UserDisplayNames> GetDisplayNamesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var name = accessor.HttpContext?.User.FindFirst("name")?.Value ?? string.Empty;
        return Task.FromResult(new UserDisplayNames(name, name));
    }
}