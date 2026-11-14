using System.Reflection;
using Lms.Api.Features.Notifications;
using Lms.Shared.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace Lms.IntegrationTests.Notifications;

/// <summary>M6 L13 / route table: the hub has no client-callable group join and no method that takes a user or conversation id.</summary>
public sealed class HubContractTests
{
    private static IReadOnlyList<MethodInfo> CallableMethods() =>
        [.. typeof(NotificationsHub).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.Name != nameof(Hub.OnConnectedAsync) && m.Name != nameof(Hub.OnDisconnectedAsync))];

    [Fact]
    public void Hub_contract_has_no_client_group_join()
    {
        var names = CallableMethods().Select(m => m.Name).ToList();

        Assert.Equal(NotificationHub.ClientCallableMethods, names);
        Assert.DoesNotContain(names, n => n.Contains("Group", StringComparison.OrdinalIgnoreCase) || n.Contains("Join", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Subscribe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Hub_methods_take_no_user_or_conversation_id()
    {
        // the one existing method re-checks the caller's own assignment; nothing accepts a user, conversation or group name
        Assert.All(CallableMethods(), m => Assert.All(m.GetParameters(), p =>
        {
            Assert.DoesNotContain("user", p.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("conversation", p.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("group", p.Name, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(typeof(string), p.ParameterType);
        }));
    }
}
