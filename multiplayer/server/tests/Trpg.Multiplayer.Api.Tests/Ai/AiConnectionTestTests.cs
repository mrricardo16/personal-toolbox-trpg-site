using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Trpg.Multiplayer.Api.Ai;
using Trpg.Multiplayer.Api.Rooms;
using Xunit;

namespace Trpg.Multiplayer.Api.Tests.Ai;

public sealed class AiConnectionTestTests
{
    private const string TestSecret = "TEST-ONLY-SECRET-DO-NOT-LEAK-123456";
    private const string PublicEndpoint = "https://api.example.test/v1/chat/completions";

    [Fact]
    public async Task TestAsync_SendsMinimalOpenAiCompatibleRequestAndReturnsSuccess()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"OK\"}}]}", Encoding.UTF8, "application/json")
        });
        var tester = CreateTester(handler, new Dictionary<string, IPAddress[]>
        {
            ["api.example.test"] = [IPAddress.Parse("93.184.216.34")]
        });

        var result = await tester.TestAsync(
            new RoomAiConfiguration("openai-compatible", PublicEndpoint, "test-model", true),
            TestSecret,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("openai-compatible", result.Provider);
        Assert.Equal("test-model", result.Model);
        Assert.Null(result.Code);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", TestSecret), handler.Request.Headers.Authorization);
        var requestBody = handler.RequestBody!;
        Assert.Contains("\"model\":\"test-model\"", requestBody, StringComparison.Ordinal);
        Assert.Contains("Connection test.", requestBody, StringComparison.Ordinal);
        Assert.DoesNotContain("scenario", requestBody, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("http://api.example.test/v1/chat/completions", "ENDPOINT_REJECTED")]
    [InlineData("https://localhost/v1/chat/completions", "ENDPOINT_REJECTED")]
    [InlineData("https://127.0.0.1/v1/chat/completions", "ENDPOINT_REJECTED")]
    [InlineData("https://[::1]/v1/chat/completions", "ENDPOINT_REJECTED")]
    [InlineData("https://10.0.0.5/v1/chat/completions", "ENDPOINT_REJECTED")]
    [InlineData("https://172.16.0.1/v1/chat/completions", "ENDPOINT_REJECTED")]
    [InlineData("https://192.168.1.1/v1/chat/completions", "ENDPOINT_REJECTED")]
    [InlineData("https://169.254.169.254/latest", "ENDPOINT_REJECTED")]
    [InlineData("https://[fe80::1]/v1/chat/completions", "ENDPOINT_REJECTED")]
    [InlineData("https://[fd00::1]/v1/chat/completions", "ENDPOINT_REJECTED")]
    [InlineData("https://user:pass@api.example.test/v1/chat/completions", "ENDPOINT_REJECTED")]
    public async Task EndpointPolicy_RejectsUnsafeTargetsBeforeHttp(string endpoint, string expectedCode)
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("HTTP must not be called."));
        var tester = CreateTester(handler, new Dictionary<string, IPAddress[]>
        {
            ["api.example.test"] = [IPAddress.Parse("93.184.216.34")]
        });

        var result = await tester.TestAsync(
            new RoomAiConfiguration("openai-compatible", endpoint, "test-model", true),
            TestSecret,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(expectedCode, result.Code);
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task EndpointPolicy_RejectsDnsResolutionToPrivateAddress()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("HTTP must not be called."));
        var tester = CreateTester(handler, new Dictionary<string, IPAddress[]>
        {
            ["evil.example.test"] = [IPAddress.Parse("10.0.0.5")]
        });

        var result = await tester.TestAsync(
            new RoomAiConfiguration("openai-compatible", "https://evil.example.test/v1/chat/completions", "test-model", true),
            TestSecret,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("ENDPOINT_REJECTED", result.Code);
        Assert.Null(handler.Request);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "PROVIDER_UNAUTHORIZED")]
    [InlineData(HttpStatusCode.Forbidden, "PROVIDER_UNAUTHORIZED")]
    [InlineData(HttpStatusCode.InternalServerError, "PROVIDER_HTTP_ERROR")]
    [InlineData(HttpStatusCode.Redirect, "REDIRECT_REJECTED")]
    public async Task TestAsync_MapsProviderStatusesToSanitizedCodes(HttpStatusCode statusCode, string expectedCode)
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(statusCode));
        var tester = CreateTester(handler, PublicAddresses());

        var result = await tester.TestAsync(
            new RoomAiConfiguration("deepseek", PublicEndpoint, "deepseek-chat", true),
            TestSecret,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(expectedCode, result.Code);
        Assert.DoesNotContain(TestSecret, JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestAsync_RejectsInvalidJsonAndOversizedBodyWithoutReturningBody()
    {
        var invalidJsonHandler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not-json", Encoding.UTF8, "application/json")
        });
        var invalidJsonResult = await CreateTester(invalidJsonHandler, PublicAddresses()).TestAsync(
            new RoomAiConfiguration("deepseek", PublicEndpoint, "deepseek-chat", true),
            TestSecret,
            CancellationToken.None);
        Assert.Equal("INVALID_RESPONSE", invalidJsonResult.Code);

        var oversizedHandler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new string('x', AiConnectionTester.MaxResponseBytes + 1), Encoding.UTF8, "application/json")
        });
        var oversizedResult = await CreateTester(oversizedHandler, PublicAddresses()).TestAsync(
            new RoomAiConfiguration("deepseek", PublicEndpoint, "deepseek-chat", true),
            TestSecret,
            CancellationToken.None);
        Assert.Equal("RESPONSE_TOO_LARGE", oversizedResult.Code);
        Assert.DoesNotContain(TestSecret, JsonSerializer.Serialize(oversizedResult), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestAsync_MapsTimeoutAndDoesNotExposeEchoedSecret()
    {
        var timeoutHandler = new RecordingHandler(_ => throw new TaskCanceledException());
        var timeoutResult = await CreateTester(timeoutHandler, PublicAddresses()).TestAsync(
            new RoomAiConfiguration("deepseek", PublicEndpoint, "deepseek-chat", true),
            TestSecret,
            CancellationToken.None);
        Assert.Equal("TIMEOUT", timeoutResult.Code);

        var echoHandler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"{{\"choices\":[{{\"message\":{{\"content\":\"{TestSecret}\"}}}}]}}", Encoding.UTF8, "application/json")
        });
        var echoResult = await CreateTester(echoHandler, PublicAddresses()).TestAsync(
            new RoomAiConfiguration("deepseek", PublicEndpoint, "deepseek-chat", true),
            TestSecret,
            CancellationToken.None);
        Assert.True(echoResult.Success);
        Assert.DoesNotContain(TestSecret, JsonSerializer.Serialize(echoResult), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectionTestGate_AllowsOnlyOneTestPerRoom()
    {
        var gate = new InMemoryRoomConnectionTestGate();
        var roomId = Guid.NewGuid();

        Assert.True(gate.TryEnter(roomId, out var lease));
        Assert.False(gate.TryEnter(roomId, out var rejectedLease));
        Assert.Null(rejectedLease);

        lease!.Dispose();
        Assert.True(gate.TryEnter(roomId, out var nextLease));
        nextLease!.Dispose();
    }

    private static AiConnectionTester CreateTester(
        HttpMessageHandler handler,
        IReadOnlyDictionary<string, IPAddress[]> addresses)
    {
        var resolver = new FakeHostAddressResolver(addresses);
        var policy = new AiEndpointPolicy(resolver);
        return new AiConnectionTester(new FakeHttpClientFactory(handler), policy);
    }

    private static IReadOnlyDictionary<string, IPAddress[]> PublicAddresses() => new Dictionary<string, IPAddress[]>
    {
        ["api.example.test"] = [IPAddress.Parse("93.184.216.34")]
    };

    private sealed class FakeHostAddressResolver(IReadOnlyDictionary<string, IPAddress[]> addresses) : IHostAddressResolver
    {
        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IPAddress>>(addresses.TryGetValue(host, out var resolved) ? resolved : []);
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? RequestBody { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Task.FromResult(handler(request));
        }
    }
}
