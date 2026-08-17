namespace Trpg.Multiplayer.Api.Ai;

public static class AiConnectionTestCodes
{
    public const string ConfigurationMissing = "CONFIGURATION_MISSING";
    public const string CredentialMissing = "CREDENTIAL_MISSING";
    public const string EndpointRejected = "ENDPOINT_REJECTED";
    public const string DnsResolutionFailed = "DNS_RESOLUTION_FAILED";
    public const string Timeout = "TIMEOUT";
    public const string NetworkError = "NETWORK_ERROR";
    public const string ProviderUnauthorized = "PROVIDER_UNAUTHORIZED";
    public const string ProviderHttpError = "PROVIDER_HTTP_ERROR";
    public const string InvalidResponse = "INVALID_RESPONSE";
    public const string ResponseTooLarge = "RESPONSE_TOO_LARGE";
    public const string RedirectRejected = "REDIRECT_REJECTED";
    public const string TestBusy = "TEST_BUSY";
}

public sealed record AiConnectionTestResult(
    bool Success,
    string? Provider,
    string? Model,
    long? LatencyMs,
    string? Code);

public interface IAiConnectionTester
{
    Task<AiConnectionTestResult> TestAsync(
        Rooms.RoomAiConfiguration configuration,
        string credential,
        CancellationToken cancellationToken);
}

public interface IHostAddressResolver
{
    Task<IReadOnlyList<System.Net.IPAddress>> ResolveAsync(
        string host,
        CancellationToken cancellationToken);
}

public interface IAiEndpointPolicy
{
    Task<AiEndpointPolicyResult> ValidateAsync(
        string endpoint,
        CancellationToken cancellationToken);
}

public sealed record AiEndpointPolicyResult(bool IsAllowed, string? Code, Uri? Uri)
{
    public static AiEndpointPolicyResult Allowed(Uri uri) => new(true, null, uri);

    public static AiEndpointPolicyResult Rejected(string code) => new(false, code, null);
}

public interface IRoomConnectionTestGate
{
    bool TryEnter(Guid roomId, out IDisposable? lease);
}
