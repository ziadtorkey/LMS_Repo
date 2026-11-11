using Lms.Domain.Identity;
using Lms.Domain.People;
using Microsoft.AspNetCore.Identity;

namespace Lms.Infrastructure.People;

public sealed class PersonAccounts(UserManager<User> users) : IPersonAccounts
{
    public async Task<(User User, bool Created)> EnsureUserAsync(
        string? email, string userName, string nameAr, string nameEn, CancellationToken cancellationToken)
    {
        var normalizedEmail = PersonNormalizer.Email(email);
        var existing = normalizedEmail is null ? null : await users.FindByEmailAsync(normalizedEmail);
        existing ??= await users.FindByNameAsync(userName);
        if (existing is not null)
        {
            return (existing, false);
        }

        var user = new User
        {
            UserName = userName,
            Email = normalizedEmail,
            DisplayNameAr = nameAr,
            DisplayNameEn = nameEn,
            LockoutEnabled = true,
        };
        var result = await users.CreateAsync(user); // no password: the user sets one through the invitation link
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "The user could not be created: " + string.Join(", ", result.Errors.Select(e => e.Code)));
        }

        return (user, true);
    }
}
