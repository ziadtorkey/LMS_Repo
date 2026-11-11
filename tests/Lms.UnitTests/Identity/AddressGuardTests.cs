using System.Net;
using Lms.Infrastructure.Identity.External;

namespace Lms.UnitTests.Identity;

public sealed class AddressGuardTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.10.20.30")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:127.0.0.1")]
    public void Internal_addresses_are_blocked(string address) =>
        Assert.True(AddressGuard.IsBlocked(IPAddress.Parse(address)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]
    [InlineData("172.15.255.255")]
    [InlineData("100.128.0.1")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]
    public void Public_addresses_are_allowed(string address) =>
        Assert.False(AddressGuard.IsBlocked(IPAddress.Parse(address)));

    [Fact]
    public void Private_network_switch_outside_Development_fails_fast() =>
        Assert.Throws<InvalidOperationException>(() => OutboundGuard.Create(isDevelopment: false, allowPrivateNetworkSetting: true));

    [Fact]
    public void Private_network_switch_is_honoured_only_in_Development()
    {
        Assert.True(OutboundGuard.Create(isDevelopment: true, allowPrivateNetworkSetting: true).AllowPrivateNetwork);
        Assert.False(OutboundGuard.Create(isDevelopment: true, allowPrivateNetworkSetting: false).AllowPrivateNetwork);
        Assert.False(OutboundGuard.Create(isDevelopment: false, allowPrivateNetworkSetting: false).AllowPrivateNetwork);
    }

    [Fact]
    public void Handler_never_follows_redirects_or_uses_a_proxy_and_has_a_short_connect_timeout()
    {
        using var handler = OutboundGuard.Create(false, false).CreateHandler();

        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseProxy);
        Assert.Equal(TimeSpan.FromSeconds(5), handler.ConnectTimeout);
        Assert.NotNull(handler.ConnectCallback);
    }
}
