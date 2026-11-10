using Lms.Domain.Identity;
using Lms.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.Infrastructure.Identity;

public static class IdentityServiceCollectionExtensions
{
    /// <summary>User store, password policy and lockout (5 failed attempts lock the account for 15 minutes).</summary>
    public static IServiceCollection AddLmsIdentityStores(this IServiceCollection services)
    {
        services.AddIdentityCore<User>(options =>
            {
                options.User.RequireUniqueEmail = true;
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

        return services;
    }
}
