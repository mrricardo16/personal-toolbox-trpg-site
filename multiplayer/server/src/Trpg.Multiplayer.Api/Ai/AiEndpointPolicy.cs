using System.Net;
using System.Net.Sockets;

namespace Trpg.Multiplayer.Api.Ai;

public sealed class DnsHostAddressResolver : IHostAddressResolver
{
    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(
        string host,
        CancellationToken cancellationToken)
    {
        return await Dns.GetHostAddressesAsync(host, cancellationToken);
    }
}

public sealed class AiEndpointPolicy(IHostAddressResolver addressResolver) : IAiEndpointPolicy
{
    public async Task<AiEndpointPolicyResult> ValidateAsync(
        string endpoint,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return AiEndpointPolicyResult.Rejected(AiConnectionTestCodes.EndpointRejected);
        }

        var host = uri.DnsSafeHost.TrimEnd('.');
        if (IsLocalhostName(host))
        {
            return AiEndpointPolicyResult.Rejected(AiConnectionTestCodes.EndpointRejected);
        }

        if (IPAddress.TryParse(host, out var literalAddress))
        {
            return IsBlockedAddress(literalAddress)
                ? AiEndpointPolicyResult.Rejected(AiConnectionTestCodes.EndpointRejected)
                : AiEndpointPolicyResult.Allowed(uri);
        }

        IReadOnlyList<IPAddress> resolvedAddresses;
        try
        {
            resolvedAddresses = await addressResolver.ResolveAsync(host, cancellationToken);
        }
        catch (SocketException)
        {
            return AiEndpointPolicyResult.Rejected(AiConnectionTestCodes.DnsResolutionFailed);
        }
        catch (TimeoutException)
        {
            return AiEndpointPolicyResult.Rejected(AiConnectionTestCodes.DnsResolutionFailed);
        }

        if (resolvedAddresses.Count == 0 || resolvedAddresses.Any(IsBlockedAddress))
        {
            return AiEndpointPolicyResult.Rejected(
                resolvedAddresses.Count == 0
                    ? AiConnectionTestCodes.DnsResolutionFailed
                    : AiConnectionTestCodes.EndpointRejected);
        }

        return AiEndpointPolicyResult.Allowed(uri);
    }

    private static bool IsLocalhostName(string host)
    {
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBlockedAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var first = bytes[0];
            var second = bytes[1];
            return first == 0
                || first == 10
                || first == 127
                || first == 169 && second == 254
                || first == 172 && second is >= 16 and <= 31
                || first == 192 && second == 168
                || first == 198 && second is >= 18 and <= 19
                || first >= 224
                || first == 100 && second is >= 64 and <= 127;
        }

        return address.Equals(IPAddress.IPv6None)
            || bytes[0] == 0xff
            || (bytes[0] & 0xfe) == 0xfc
            || bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80;
    }
}
