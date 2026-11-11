using System.Globalization;
using Bunit;
using Lms.Client.Resources;
using Lms.Client.Services;
using Lms.Client.Services.Api;
using Lms.Client.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Lms.Client.Tests.Services;

public sealed class ApiCallsTests : ClientTestContext
{
    private NavigationManager Nav => Services.GetRequiredService<NavigationManager>();
    private IStringLocalizer<SharedResource> Loc => Services.GetRequiredService<IStringLocalizer<SharedResource>>();

    private async Task<(bool Ok, List<string> Errors)> RunAsync(Func<Task> action)
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        var errors = new List<string>();
        var ok = await ApiCalls.RunAsync(action, Nav, Loc, errors.Add);
        return (ok, errors);
    }

    [Fact]
    public async Task Success_returns_true_without_errors()
    {
        var (ok, errors) = await RunAsync(() => Task.CompletedTask);
        Assert.True(ok);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task Problem_is_reported_with_its_error_resource()
    {
        var (ok, errors) = await RunAsync(() => throw new ApiProblemException(409, "code_taken", null, null));
        Assert.False(ok);
        Assert.Equal("This code is already used.", Assert.Single(errors));
    }

    [Fact]
    public async Task Policy_block_reports_the_server_detail()
    {
        var (_, errors) = await RunAsync(() => throw new ApiProblemException(403, "BlockedByPolicy", "Blocked by policy", null));
        Assert.Equal("Blocked by policy", Assert.Single(errors));
    }

    [Fact]
    public async Task Stale_context_goes_to_chooser_without_an_error_message()
    {
        var (ok, errors) = await RunAsync(() => throw new ApiContextException("invalid_context"));
        Assert.False(ok);
        Assert.Empty(errors);
        Assert.EndsWith("/choose-context", Nav.Uri);
    }

    [Fact]
    public async Task Forbidden_goes_to_the_forbidden_page_without_an_error_message()
    {
        var (_, errors) = await RunAsync(() => throw new ApiForbiddenException());
        Assert.Empty(errors);
        Assert.EndsWith("/forbidden", Nav.Uri);
    }

    [Fact]
    public async Task Network_failure_reports_the_generic_message()
    {
        var (ok, errors) = await RunAsync(() => throw new HttpRequestException("offline"));
        Assert.False(ok);
        Assert.Equal("Something went wrong. Try again.", Assert.Single(errors));
    }

    [Fact]
    public async Task Other_exceptions_are_not_swallowed()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(() => throw new InvalidOperationException("bug")));
    }
}
