using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Trpg.Multiplayer.Api.Rooms;

namespace Trpg.Multiplayer.Api.Ai;

public sealed class AiConnectionTester(
    IHttpClientFactory httpClientFactory,
    IAiEndpointPolicy endpointPolicy) : IAiConnectionTester
{
    public const string HttpClientName = "ai-connection-test";
    public const int MaxResponseBytes = 64 * 1024;
    public static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(10);

    public async Task<AiConnectionTestResult> TestAsync(
        RoomAiConfiguration configuration,
        string credential,
        CancellationToken cancellationToken)
    {
        if (!configuration.CredentialPresent || string.IsNullOrWhiteSpace(credential))
        {
            return Failure(configuration, AiConnectionTestCodes.CredentialMissing);
        }

        var payload = JsonSerializer.Serialize(new
        {
            model = configuration.Model,
            messages = new[]
            {
                new { role = "user", content = "Connection test." }
            },
            temperature = 0
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, (Uri?)null)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);

        var stopwatch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ConnectionTimeout);
        try
        {
            var endpoint = await endpointPolicy.ValidateAsync(configuration.Endpoint, timeout.Token);
            if (!endpoint.IsAllowed || endpoint.Uri is null)
            {
                return Failure(configuration, endpoint.Code ?? AiConnectionTestCodes.EndpointRejected);
            }

            request.RequestUri = endpoint.Uri;
            using var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            stopwatch.Stop();

            if ((int)response.StatusCode is >= 300 and <= 399)
            {
                return Failure(configuration, AiConnectionTestCodes.RedirectRejected, stopwatch.ElapsedMilliseconds);
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return Failure(configuration, AiConnectionTestCodes.ProviderUnauthorized, stopwatch.ElapsedMilliseconds);
            }

            if (!response.IsSuccessStatusCode)
            {
                return Failure(configuration, AiConnectionTestCodes.ProviderHttpError, stopwatch.ElapsedMilliseconds);
            }

            var body = await ReadBoundedBodyAsync(response, timeout.Token);
            if (body is null || !HasCompatibleResponse(body))
            {
                return Failure(
                    configuration,
                    body is null ? AiConnectionTestCodes.ResponseTooLarge : AiConnectionTestCodes.InvalidResponse,
                    stopwatch.ElapsedMilliseconds);
            }

            return new AiConnectionTestResult(
                true,
                configuration.Provider,
                configuration.Model,
                stopwatch.ElapsedMilliseconds,
                null);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return Failure(configuration, AiConnectionTestCodes.Timeout, stopwatch.ElapsedMilliseconds);
        }
        catch (HttpRequestException)
        {
            stopwatch.Stop();
            return Failure(configuration, AiConnectionTestCodes.NetworkError, stopwatch.ElapsedMilliseconds);
        }
    }

    private static async Task<string?> ReadBoundedBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > MaxResponseBytes)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var body = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (body.Length + read > MaxResponseBytes)
            {
                return null;
            }

            body.Write(buffer, 0, read);
        }

        return Encoding.UTF8.GetString(body.ToArray());
    }

    private static bool HasCompatibleResponse(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                return false;
            }

            var firstChoice = choices[0];
            if (firstChoice.TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String)
            {
                return true;
            }

            return firstChoice.TryGetProperty("text", out var text)
                && text.ValueKind == JsonValueKind.String;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static AiConnectionTestResult Failure(
        RoomAiConfiguration configuration,
        string code,
        long? latencyMs = null) => new(
        false,
        configuration.Provider,
        configuration.Model,
        latencyMs,
        code);
}
