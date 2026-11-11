using Lms.Domain.Identity;
using Lms.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lms.Infrastructure.Identity;

public static class IdentityServiceCollectionExtensions
{
    /// <summary>User store, password policy and lockout (5 failed attempts lock the account for 15 minutes).</summary>
    public static IServiceCollection AddLmsIdentityStores(this IServiceCollection services)
    {
        services.AddIdentityCore<User>(options =>
            {
                // Email is optional (students have none); LmsUserValidator enforces "valid and unique when present".
                options.User.RequireUniqueEmail = false;
                // The user name is the email, which is validated separately: apostrophes and non-ASCII letters are legal.
                options.User.AllowedUserNameCharacters = string.Empty;
                options.Password.RequiredLength = 8;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireDigit = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<LmsDbContext>();

        services.RemoveAll<IUserValidator<User>>();
        services.AddScoped<IUserValidator<User>, LmsUserValidator>();

        services.AddScoped<IUserProfileReader, UserStoreProfileReader>();
        return services;
    }
}
