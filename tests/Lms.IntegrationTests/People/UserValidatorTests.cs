using Lms.Domain.Identity;
using Lms.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.People;

[Collection(ApiCollection.Name)]
public sealed class UserValidatorTests(LmsApiFactory factory)
{
    private static User NewUser(string userName, string? email) => new()
    {
        UserName = userName,
        Email = email,
        DisplayNameAr = "مستخدم",
        DisplayNameEn = "User",
        SecurityStamp = Guid.NewGuid().ToString("N"),
    };

    [Fact]
    public async Task User_without_email_is_valid_and_duplicate_email_is_rejected()
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var email = $"v-{Guid.NewGuid():N}@lms.test";

        // Two students without email: both fine.
        Assert.True((await users.CreateAsync(NewUser("stu-" + Guid.NewGuid().ToString("N"), null))).Succeeded);
        Assert.True((await users.CreateAsync(NewUser("stu-" + Guid.NewGuid().ToString("N"), null))).Succeeded);

        Assert.True((await users.CreateAsync(NewUser(email, email))).Succeeded);
        var duplicate = await users.CreateAsync(NewUser("other-" + Guid.NewGuid().ToString("N"), email.ToUpperInvariant()));

        Assert.False(duplicate.Succeeded);
        Assert.Contains(duplicate.Errors, e => e.Code == "DuplicateEmail");
    }

    [Fact]
    public async Task User_name_is_required_and_unique_and_a_present_email_must_be_valid()
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var name = "dup-" + Guid.NewGuid().ToString("N");
        Assert.True((await users.CreateAsync(NewUser(name, null))).Succeeded);

        var sameName = await users.CreateAsync(NewUser(name, null));
        var noName = await users.CreateAsync(NewUser(string.Empty, null));
        var badEmail = await users.CreateAsync(NewUser("x-" + Guid.NewGuid().ToString("N"), "not-an-email"));

        Assert.Contains(sameName.Errors, e => e.Code == "DuplicateUserName");
        Assert.Contains(noName.Errors, e => e.Code == "InvalidUserName");
        Assert.Contains(badEmail.Errors, e => e.Code == "InvalidEmail");
    }

    [Fact]
    public async Task Updating_a_user_does_not_collide_with_itself()
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var email = $"self-{Guid.NewGuid():N}@lms.test";
        var user = NewUser(email, email);
        Assert.True((await users.CreateAsync(user)).Succeeded);

        user.DisplayNameEn = "Renamed";

        Assert.True((await users.UpdateAsync(user)).Succeeded);
    }
}
