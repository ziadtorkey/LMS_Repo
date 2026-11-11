using System.Net.Mail;
using Lms.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace Lms.Infrastructure.Identity;

/// <summary>
/// User rules of the LMS: a user name is required; an email is optional (students have none) but, when present, must be
/// valid and unique among users. Replaces the stock "email required and unique" rule.
/// </summary>
public sealed class LmsUserValidator : IUserValidator<User>
{
    public async Task<IdentityResult> ValidateAsync(UserManager<User> manager, User user)
    {
        var errors = new List<IdentityError>();
        if (string.IsNullOrWhiteSpace(user.UserName))
        {
            errors.Add(new IdentityError { Code = "InvalidUserName", Description = "A user name is required." });
        }
        else if (await manager.FindByNameAsync(user.UserName) is { } sameName && sameName.Id != user.Id)
        {
            errors.Add(new IdentityError { Code = "DuplicateUserName", Description = "The user name is already taken." });
        }

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            if (!MailAddress.TryCreate(user.Email, out var parsed) || parsed.Address != user.Email)
            {
                errors.Add(new IdentityError { Code = "InvalidEmail", Description = "The email is not valid." });
            }
            else if (await manager.FindByEmailAsync(user.Email) is { } sameEmail && sameEmail.Id != user.Id)
            {
                errors.Add(new IdentityError { Code = "DuplicateEmail", Description = "The email is already used." });
            }
        }

        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]);
    }
}
