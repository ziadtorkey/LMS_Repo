using Lms.Api.Infrastructure;
using Lms.Domain.Access;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Lms.IntegrationTests.Security;

/// <summary>RequireAnyPermission (M5 Task 17): each listed permission opens the route, an unlisted one does not.</summary>
public sealed class AnyPermissionFilterTests
{
    private sealed class OnlyEvaluator(string granted) : IAccessEvaluator
    {
        public Task<AccessDecision> EvaluateAsync(string permission, string? resourceOrgUnitPath, CancellationToken cancellationToken) =>
            Task.FromResult(permission == granted ? AccessDecision.Allow : AccessDecision.Deny(AccessDenialReason.MissingPermission));
    }

    private static async Task<bool> PassesAsync(string granted, params string[] listed)
    {
        var services = new ServiceCollection().AddSingleton<IAccessEvaluator>(new OnlyEvaluator(granted)).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var passed = false;
        var filter = new AnyPermissionEndpointFilter(listed);

        await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(http), _ =>
        {
            passed = true;
            return ValueTask.FromResult<object?>(null);
        });

        return passed;
    }

    [Theory]
    [InlineData("Attendance.Take")]
    [InlineData("Attendance.Edit")]
    [InlineData("Attendance.View")]
    public async Task Every_listed_permission_opens_the_route(string granted) =>
        Assert.True(await PassesAsync(granted, "Attendance.Take", "Attendance.Edit", "Attendance.View"));

    [Theory]
    [InlineData("Attendance.ViewOwn")]
    [InlineData("Grades.View")]
    public async Task An_unlisted_permission_does_not(string granted) =>
        Assert.False(await PassesAsync(granted, "Attendance.Take", "Attendance.Edit", "Attendance.View"));
}
