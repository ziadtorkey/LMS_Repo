using System.Net;
using System.Net.Sockets;

namespace Lms.Infrastructure.Identity.External;

/// <summary>Which addresses a server-side request to a client-supplied provider URL may never reach (SSRF, #185).</summary>
public static class AddressGuard
{
    public static bool IsBlocked(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.IPv6None) || address.Equals(IPAddress.None))
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || address.IsIPv6UniqueLocal;
        }

        var b = address.GetAddressBytes();
        return b[0] == 0                                  // "this" network
               || b[0] == 10                              // RFC 1918
               || (b[0] == 172 && b[1] is >= 16 and <= 31)
               || (b[0] == 192 && b[1] == 168)
               || (b[0] == 169 && b[1] == 254)            // link-local, cloud metadata
               || (b[0] == 100 && b[1] is >= 64 and <= 127) // carrier-grade NAT
               || b[0] >= 224;                            // multicast and reserved
    }
}

/// <summary>Raised when a provider URL resolves to an internal address; carries no address so nothing leaks to the caller.</summary>
public sealed class BlockedAddressException() : IOException("authority_not_allowed");

/// <summary>
/// Builds the HTTP handler used for every request the server makes to a provider (discovery test, OpenID Connect backchannel):
/// no redirects, short connect timeout, and, unless <c>ExternalIdentity:AllowPrivateNetwork</c> is on (Development only),
/// the host is resolved here and refused when any of its addresses is internal. The connection then goes to the address that
/// was checked, so a second DNS answer cannot swap it.
/// </summary>
public sealed class OutboundGuard(bool allowPrivateNetwork)
{
    public bool AllowPrivateNetwork { get; } = allowPrivateNetwork;

    public SocketsHttpHandler CreateHandler()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
        };

        if (!AllowPrivateNetwork)
        {
            handler.ConnectCallback = ConnectAsync;
        }

        return handler;
    }

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
        if (addresses.Length == 0 || addresses.Any(AddressGuard.IsBlocked))
        {
            throw new BlockedAddressException();
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
