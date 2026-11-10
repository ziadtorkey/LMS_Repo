using Lms.Domain.Identity;
using Lms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Lms.Infrastructure.Identity;

/// <summary>Reads the Arabic and English display names from the user store.</summary>
public sealed class UserStoreProfileReader(LmsDbContext db) : IUserProfileReader
{
    public async Task<UserDisplayNames> GetDisplayNamesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var names = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new UserDisplayNames(u.DisplayNameAr, u.DisplayNameEn))
            .SingleOrDefaultAsync(cancellationToken);

        // A token whose user no longer exists is rejected earlier; empty names are only a safe default.
        return names ?? new UserDisplayNames(string.Empty, string.Empty);
    }
}
