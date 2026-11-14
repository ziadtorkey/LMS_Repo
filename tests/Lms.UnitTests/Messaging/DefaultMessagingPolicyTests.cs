using Lms.Domain.Files;
using Lms.Domain.Messaging;
using Lms.Infrastructure.Messaging;
using Lms.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.UnitTests.Messaging;

/// <summary>Until Task 11 plugs in the D69 matrix the default policy fails closed (QA2 on #733), and the antivirus hook exists but is off (D65).</summary>
public sealed class DefaultMessagingPolicyTests
{
    [Fact]
    public async Task The_default_policy_refuses_every_pair_member_and_post()
    {
        var policy = new ServiceCollection().AddMessaging().BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IMessagingPolicy>();
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());

        var start = await policy.CanStartAsync(a, b, default); // student to student, admin to student: nobody until the matrix exists
        var add = await policy.CanAddToGroupAsync(a, b, default);
        var post = await policy.CanPostAsync(a, Conversation.Direct(Guid.NewGuid(), a, b, DateTime.UtcNow), default);

        Assert.IsType<DenyAllMessagingPolicy>(policy);
        Assert.All(new[] { start, add, post }, d =>
        {
            Assert.False(d.Allowed);
            Assert.Equal(ProblemCodes.messaging_not_allowed, d.Reason);
        });
    }

    [Fact]
    public async Task The_scanner_hook_is_registered_and_off_by_default()
    {
        var scanner = new ServiceCollection().AddMessaging().BuildServiceProvider().GetRequiredService<IFileScanner>();

        Assert.IsType<NoOpFileScanner>(scanner);
        Assert.True(await scanner.IsCleanAsync("a.pdf", new MemoryStream([1, 2, 3]), default));
    }
}
