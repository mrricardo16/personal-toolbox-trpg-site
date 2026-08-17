using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Trpg.Multiplayer.Api.Realtime;
using Trpg.Multiplayer.Api.Rooms;
using Xunit;

namespace Trpg.Multiplayer.Api.Tests;

public sealed class RoomAiConfigurationApiTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private const string TestSecret = "TEST-ONLY-SECRET-DO-NOT-LEAK-123456";
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(5);
    private readonly List<HubConnection> connections = [];

    [Fact]
    public async Task PutAiConfig_HostStoresSecretAndReturnsOnlyPublicConfiguration()
    {
        var room = await CreateRoomAsync("Host");

        using var response = await SendAuthorizedAsync(
            HttpMethod.Put,
            $"/api/rooms/{room.RoomId}/ai-config",
            room.PlayerSessionToken,
            new { provider = "deepseek", model = "deepseek-chat", apiKey = TestSecret });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(TestSecret, responseBody, StringComparison.Ordinal);
        Assert.DoesNotContain("apiKey", responseBody, StringComparison.OrdinalIgnoreCase);
        var configuration = JsonSerializer.Deserialize<RoomAiConfiguration>(
            responseBody,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(
            new RoomAiConfiguration(
                "deepseek",
                RoomAiProviders.DeepSeekEndpoint,
                "deepseek-chat",
                true),
            configuration);

        var credentials = factory.Services.GetRequiredService<IRoomCredentialStore>();
        Assert.True(credentials.TryGet(room.RoomId, out var storedCredential));
        Assert.Equal(TestSecret, storedCredential);

        var canonical = GetRoom(room.RoomId);
        var canonicalJson = JsonSerializer.Serialize(canonical);
        var snapshotJson = JsonSerializer.Serialize(ToSnapshot(canonical));
        Assert.DoesNotContain(TestSecret, canonicalJson, StringComparison.Ordinal);
        Assert.DoesNotContain(TestSecret, snapshotJson, StringComparison.Ordinal);
        Assert.DoesNotContain(
            typeof(RoomSession).GetProperties(BindingFlags.Instance | BindingFlags.Public),
            property => property.Name.Contains("ApiKey", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PutAiConfig_OpenAiCompatibleRetainsCustomEndpointAndModel()
    {
        var room = await CreateRoomAsync("Host");
        const string endpoint = "https://ai.example.test/v1/chat/completions";

        using var response = await SendAuthorizedAsync(
            HttpMethod.Put,
            $"/api/rooms/{room.RoomId}/ai-config",
            room.PlayerSessionToken,
            new
            {
                provider = "openai-compatible",
                endpoint,
                model = "test-model",
                apiKey = TestSecret
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var configuration = await response.Content.ReadFromJsonAsync<RoomAiConfiguration>();
        Assert.Equal(new RoomAiConfiguration("openai-compatible", endpoint, "test-model", true), configuration);
    }

    [Fact]
    public async Task PutAiConfig_EnforcesAuthenticationRoomAndHostBoundaries()
    {
        var firstRoom = await CreateRoomAsync("First Host");
        var member = await JoinRoomAsync(firstRoom.InviteCode, "Member");
        var secondRoom = await CreateRoomAsync("Second Host");
        var payload = new { provider = "deepseek", model = "deepseek-chat", apiKey = TestSecret };

        using var missingToken = await factory.CreateClient().PutAsJsonAsync(
            $"/api/rooms/{firstRoom.RoomId}/ai-config",
            payload);
        using var invalidToken = await SendAuthorizedAsync(
            HttpMethod.Put,
            $"/api/rooms/{firstRoom.RoomId}/ai-config",
            "invalid-token",
            payload);
        using var memberResponse = await SendAuthorizedAsync(
            HttpMethod.Put,
            $"/api/rooms/{firstRoom.RoomId}/ai-config",
            member.PlayerSessionToken,
            payload);
        using var crossRoom = await SendAuthorizedAsync(
            HttpMethod.Put,
            $"/api/rooms/{secondRoom.RoomId}/ai-config",
            firstRoom.PlayerSessionToken,
            payload);

        var missingRoomId = Guid.NewGuid();
        var sameRoomHostToken = factory.Services
            .GetRequiredService<IPlayerSessionStore>()
            .Create(Guid.NewGuid(), missingRoomId, isHost: true);
        using var missingRoom = await SendAuthorizedAsync(
            HttpMethod.Put,
            $"/api/rooms/{missingRoomId}/ai-config",
            sameRoomHostToken,
            payload);

        Assert.Equal(HttpStatusCode.Unauthorized, missingToken.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidToken.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, memberResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossRoom.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingRoom.StatusCode);
    }

    [Fact]
    public async Task PutAiConfig_RejectsEmptyCredentialWithoutLeakingItInPublicError()
    {
        var room = await CreateRoomAsync("Host");

        using var emptyCredential = await SendAuthorizedAsync(
            HttpMethod.Put,
            $"/api/rooms/{room.RoomId}/ai-config",
            room.PlayerSessionToken,
            new { provider = "deepseek", model = "deepseek-chat", apiKey = "   " });
        using var invalidProvider = await SendAuthorizedAsync(
            HttpMethod.Put,
            $"/api/rooms/{room.RoomId}/ai-config",
            room.PlayerSessionToken,
            new { provider = "unsupported", model = "test", apiKey = TestSecret });

        Assert.Equal(HttpStatusCode.BadRequest, emptyCredential.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidProvider.StatusCode);
        Assert.DoesNotContain(TestSecret, await invalidProvider.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PutAiConfig_IdempotentMetadataPreservesRevisionButReplacementIncrementsIt()
    {
        var room = await CreateRoomAsync("Host");
        var path = $"/api/rooms/{room.RoomId}/ai-config";
        var publicConfig = new { provider = "deepseek", model = "deepseek-chat" };

        using var initial = await SendAuthorizedAsync(
            HttpMethod.Put,
            path,
            room.PlayerSessionToken,
            new { provider = "deepseek", model = "deepseek-chat", apiKey = "old-test-secret" });
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        Assert.Equal(2, GetRoom(room.RoomId).Revision);

        using var repeated = await SendAuthorizedAsync(
            HttpMethod.Put,
            path,
            room.PlayerSessionToken,
            publicConfig);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(2, GetRoom(room.RoomId).Revision);

        using var replaced = await SendAuthorizedAsync(
            HttpMethod.Put,
            path,
            room.PlayerSessionToken,
            new { provider = "deepseek", model = "deepseek-chat", apiKey = TestSecret });
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        Assert.Equal(3, GetRoom(room.RoomId).Revision);
        Assert.True(factory.Services.GetRequiredService<IRoomCredentialStore>()
            .TryGet(room.RoomId, out var storedCredential));
        Assert.Equal(TestSecret, storedCredential);
    }

    [Fact]
    public async Task DeleteCredential_RemovesSecretAndRetainsPublicMetadata()
    {
        var room = await CreateRoomAsync("Host");
        await ConfigureAsync(room);

        using var response = await SendAuthorizedAsync(
            HttpMethod.Delete,
            $"/api/rooms/{room.RoomId}/credential",
            room.PlayerSessionToken,
            body: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var configuration = await response.Content.ReadFromJsonAsync<RoomAiConfiguration>();
        Assert.Equal(
            new RoomAiConfiguration(
                "deepseek",
                RoomAiProviders.DeepSeekEndpoint,
                "deepseek-chat",
                false),
            configuration);
        Assert.False(factory.Services.GetRequiredService<IRoomCredentialStore>().Exists(room.RoomId));
        Assert.Equal(3, GetRoom(room.RoomId).Revision);
    }

    [Fact]
    public async Task MemberLeavePreservesCredentialAndHostLeaveRemovesIt()
    {
        var room = await CreateRoomAsync("Host");
        await ConfigureAsync(room);
        var member = await JoinRoomAsync(room.InviteCode, "Member");
        var credentials = factory.Services.GetRequiredService<IRoomCredentialStore>();

        using var memberLeave = await SendAuthorizedAsync(
            HttpMethod.Post,
            $"/api/rooms/{room.RoomId}/leave",
            member.PlayerSessionToken,
            body: null);
        Assert.Equal(HttpStatusCode.OK, memberLeave.StatusCode);
        Assert.True(credentials.Exists(room.RoomId));

        using var hostLeave = await SendAuthorizedAsync(
            HttpMethod.Post,
            $"/api/rooms/{room.RoomId}/leave",
            room.PlayerSessionToken,
            body: null);
        Assert.Equal(HttpStatusCode.OK, hostLeave.StatusCode);
        Assert.False(credentials.Exists(room.RoomId));
    }

    [Fact]
    public async Task HostDisconnect_PreservesCredential()
    {
        var room = await CreateRoomAsync("Host");
        await ConfigureAsync(room);
        var connection = CreateHubConnection();
        await connection.StartAsync();
        await connection.InvokeAsync<RoomSnapshot>("AttachSession", room.PlayerSessionToken);

        await connection.StopAsync();
        await WaitForDisconnectedAsync(room.RoomId, room.PlayerId);

        Assert.True(factory.Services.GetRequiredService<IRoomCredentialStore>().Exists(room.RoomId));
    }

    [Fact]
    public async Task PutAiConfig_BroadcastsSafeRoomSnapshotWithoutSecret()
    {
        var room = await CreateRoomAsync("Host");
        var connection = CreateHubConnection();
        await connection.StartAsync();
        await connection.InvokeAsync<RoomSnapshot>("AttachSession", room.PlayerSessionToken);
        var delivered = new TaskCompletionSource<RoomSnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<RoomSnapshot>("RoomSnapshot", snapshot =>
        {
            if (snapshot.AiConfiguration is not null)
            {
                delivered.TrySetResult(snapshot);
            }
        });

        await ConfigureAsync(room);
        var snapshot = await delivered.Task.WaitAsync(EventTimeout);

        Assert.Equal(3, snapshot.Revision);
        Assert.True(snapshot.AiConfiguration!.CredentialPresent);
        var payload = JsonSerializer.Serialize(snapshot);
        Assert.DoesNotContain(TestSecret, payload, StringComparison.Ordinal);
        Assert.DoesNotContain("apiKey", payload, StringComparison.OrdinalIgnoreCase);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var connection in connections)
        {
            await connection.DisposeAsync();
        }
    }

    private async Task ConfigureAsync(RoomCreatedResponse room)
    {
        using var response = await SendAuthorizedAsync(
            HttpMethod.Put,
            $"/api/rooms/{room.RoomId}/ai-config",
            room.PlayerSessionToken,
            new { provider = "deepseek", model = "deepseek-chat", apiKey = TestSecret });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<RoomCreatedResponse> CreateRoomAsync(string nickname)
    {
        using var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/rooms",
            new { nickname, maxPlayers = 4 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<RoomCreatedResponse>(
            await response.Content.ReadFromJsonAsync<RoomCreatedResponse>());
    }

    private async Task<RoomJoinedResponse> JoinRoomAsync(string inviteCode, string nickname)
    {
        using var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/rooms/join",
            new { inviteCode, nickname });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<RoomJoinedResponse>(
            await response.Content.ReadFromJsonAsync<RoomJoinedResponse>());
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpMethod method,
        string path,
        string token,
        object? body)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await factory.CreateClient().SendAsync(request);
    }

    private HubConnection CreateHubConnection()
    {
        var server = factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "/hubs/room"), options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        connections.Add(connection);
        return connection;
    }

    private RoomSession GetRoom(Guid roomId)
    {
        Assert.True(factory.Services.GetRequiredService<IRoomStore>().TryGet(roomId, out var room));
        return Assert.IsType<RoomSession>(room);
    }

    private RoomSnapshot ToSnapshot(RoomSession room)
    {
        return RoomSnapshotMapper.ToSnapshot(
            room,
            factory.Services.GetRequiredService<IInviteCodeRegistry>());
    }

    private async Task WaitForDisconnectedAsync(Guid roomId, Guid playerId)
    {
        using var timeout = new CancellationTokenSource(EventTimeout);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        while (!timeout.IsCancellationRequested)
        {
            var player = GetRoom(roomId).Players.Single(candidate => candidate.PlayerId == playerId);
            if (!player.IsConnected)
            {
                return;
            }

            try
            {
                await timer.WaitForNextTickAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        throw new TimeoutException("Host disconnect was not reflected in canonical room state.");
    }
}
