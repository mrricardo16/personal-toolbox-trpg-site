using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Trpg.Multiplayer.Api.Realtime;
using Trpg.Multiplayer.Api.Rooms;
using Xunit;

namespace Trpg.Multiplayer.Api.Tests.Realtime;

public sealed class DisconnectReconnectTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(5);
    private readonly List<HubConnection> connections = [];

    [Fact]
    public async Task Disconnect_PreservesMembershipAndReadyStateButSetsConnectedFalse()
    {
        var room = await CreateRoomAsync("Host");
        var player = await JoinRoomAsync(room.InviteCode, "Player");
        var (connection, attached) = await AttachAsync(player.PlayerSessionToken);

        Assert.Equal(3, attached.Revision);
        Assert.True(Member(attached, player.PlayerId).IsConnected);

        var ready = await SetReadyAsync(room.RoomId, player.PlayerSessionToken, true);
        Assert.Equal(4, ready.Revision);
        Assert.True(Member(ready, player.PlayerId).IsReady);

        await StopAndWaitAsync(connection);
        var canonical = await WaitForCanonicalPlayerAsync(room.RoomId, player.PlayerId, isConnected: false, expectedRevision: 5);
        Assert.True(canonical.Players.Single(candidate => candidate.PlayerId == player.PlayerId).IsReady);

        var disconnected = await SetReadyAsync(room.RoomId, player.PlayerSessionToken, true);
        var disconnectedPlayer = Member(disconnected, player.PlayerId);
        Assert.Equal(5, disconnected.Revision);
        Assert.True(disconnectedPlayer.IsReady);
        Assert.False(disconnectedPlayer.IsConnected);
        Assert.Equal(2, disconnected.Players.Count);
    }

    [Fact]
    public async Task SameToken_ReattachRestoresConnectedWithoutDuplicatingPlayer()
    {
        var room = await CreateRoomAsync("Host");
        var (firstConnection, firstAttach) = await AttachAsync(room.PlayerSessionToken);

        Assert.Equal(2, firstAttach.Revision);
        await StopAndWaitAsync(firstConnection);
        await WaitForCanonicalPlayerAsync(room.RoomId, room.PlayerId, isConnected: false, expectedRevision: 3);

        var (_, reattached) = await AttachAsync(room.PlayerSessionToken);

        Assert.Equal(4, reattached.Revision);
        var player = Assert.Single(reattached.Players);
        Assert.Equal(room.PlayerId, player.PlayerId);
        Assert.True(player.IsConnected);
    }

    [Fact]
    public async Task HostDisconnect_PreservesRoomForLaterJoin()
    {
        var room = await CreateRoomAsync("Host");
        var (hostConnection, attached) = await AttachAsync(room.PlayerSessionToken);

        Assert.Equal(2, attached.Revision);
        await StopAndWaitAsync(hostConnection);
        await WaitForCanonicalPlayerAsync(room.RoomId, room.PlayerId, isConnected: false, expectedRevision: 3);

        var joined = await JoinRoomAsync(room.InviteCode, "Player");

        Assert.Equal(4, joined.Room.Revision);
        Assert.Equal(2, joined.Room.Players.Count);
        Assert.False(Member(joined.Room, room.PlayerId).IsConnected);
        Assert.False(Member(joined.Room, joined.PlayerId).IsConnected);
    }

    [Fact]
    public async Task TwoConnections_KeepConnectedTrueUntilLastConnectionCloses()
    {
        var room = await CreateRoomAsync("Host");
        var (firstConnection, firstAttach) = await AttachAsync(room.PlayerSessionToken);
        var (secondConnection, secondAttach) = await AttachAsync(room.PlayerSessionToken);

        Assert.Equal(2, firstAttach.Revision);
        Assert.Equal(2, secondAttach.Revision);
        Assert.True(Member(secondAttach, room.PlayerId).IsConnected);

        await StopAndWaitAsync(firstConnection);
        await WaitForCanonicalPlayerAsync(room.RoomId, room.PlayerId, isConnected: true, expectedRevision: 2);
        var afterFirstClose = await SetReadyAsync(room.RoomId, room.PlayerSessionToken, false);
        Assert.Equal(2, afterFirstClose.Revision);
        Assert.True(Member(afterFirstClose, room.PlayerId).IsConnected);

        await StopAndWaitAsync(secondConnection);
        await WaitForCanonicalPlayerAsync(room.RoomId, room.PlayerId, isConnected: false, expectedRevision: 3);
        var afterLastClose = await SetReadyAsync(room.RoomId, room.PlayerSessionToken, false);
        Assert.Equal(3, afterLastClose.Revision);
        Assert.False(Member(afterLastClose, room.PlayerId).IsConnected);
    }

    [Fact]
    public async Task ExplicitLeave_RejectsSameTokenReattach()
    {
        var room = await CreateRoomAsync("Host");
        var player = await JoinRoomAsync(room.InviteCode, "Player");
        var (_, attached) = await AttachAsync(player.PlayerSessionToken);
        Assert.True(Member(attached, player.PlayerId).IsConnected);

        using var leaveResponse = await PostAuthorizedAsync(
            $"/api/rooms/{room.RoomId}/leave",
            player.PlayerSessionToken,
            body: null);

        Assert.Equal(HttpStatusCode.OK, leaveResponse.StatusCode);
        var leaveSnapshot = Assert.IsType<RoomSnapshot>(await leaveResponse.Content.ReadFromJsonAsync<RoomSnapshot>());
        Assert.Equal(4, leaveSnapshot.Revision);
        Assert.DoesNotContain(leaveSnapshot.Players, candidate => candidate.PlayerId == player.PlayerId);

        await AssertAttachRejectedAsync(player.PlayerSessionToken);
    }

    [Fact]
    public async Task HostClose_RejectsEveryOldRoomToken()
    {
        var room = await CreateRoomAsync("Host");
        var player = await JoinRoomAsync(room.InviteCode, "Player");
        var (hostConnection, _) = await AttachAsync(room.PlayerSessionToken);
        var (playerConnection, _) = await AttachAsync(player.PlayerSessionToken);
        var hostClosed = NewCompletion<RoomClosedEvent>();
        var playerClosed = NewCompletion<RoomClosedEvent>();
        hostConnection.On<RoomClosedEvent>("RoomClosed", message => hostClosed.TrySetResult(message));
        playerConnection.On<RoomClosedEvent>("RoomClosed", message => playerClosed.TrySetResult(message));

        using var leaveResponse = await PostAuthorizedAsync(
            $"/api/rooms/{room.RoomId}/leave",
            room.PlayerSessionToken,
            body: null);

        Assert.Equal(HttpStatusCode.OK, leaveResponse.StatusCode);
        Assert.Equal(room.RoomId, (await hostClosed.Task.WaitAsync(EventTimeout)).RoomId);
        Assert.Equal(room.RoomId, (await playerClosed.Task.WaitAsync(EventTimeout)).RoomId);
        await AssertAttachRejectedAsync(room.PlayerSessionToken);
        await AssertAttachRejectedAsync(player.PlayerSessionToken);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var connection in connections)
        {
            await connection.DisposeAsync();
        }
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

    private async Task<(HubConnection Connection, RoomSnapshot Snapshot)> AttachAsync(string playerSessionToken)
    {
        var connection = CreateHubConnection();
        await connection.StartAsync();
        var snapshot = await connection.InvokeAsync<RoomSnapshot>("AttachSession", playerSessionToken);
        return (connection, snapshot);
    }

    private async Task StopAndWaitAsync(HubConnection connection)
    {
        var closed = NewCompletion();
        connection.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };

        await connection.StopAsync();
        await closed.Task.WaitAsync(EventTimeout);
    }

    private async Task<RoomSession> WaitForCanonicalPlayerAsync(
        Guid roomId,
        Guid playerId,
        bool isConnected,
        long expectedRevision)
    {
        var roomStore = factory.Services.GetRequiredService<IRoomStore>();
        using var timeout = new CancellationTokenSource(EventTimeout);
        using var pollTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));

        while (!timeout.IsCancellationRequested)
        {
            if (roomStore.TryGet(roomId, out var room)
                && room is not null
                && room.Revision == expectedRevision
                && room.Players.SingleOrDefault(player => player.PlayerId == playerId) is { } player
                && player.IsConnected == isConnected)
            {
                return room;
            }

            try
            {
                await pollTimer.WaitForNextTickAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        throw new TimeoutException(
            $"Timed out waiting for room {roomId} player {playerId} to have IsConnected={isConnected} and Revision={expectedRevision}.");
    }

    private async Task AssertAttachRejectedAsync(string playerSessionToken)
    {
        var connection = CreateHubConnection();
        await connection.StartAsync();
        await Assert.ThrowsAsync<HubException>(
            () => connection.InvokeAsync<RoomSnapshot>("AttachSession", playerSessionToken));
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

    private async Task<RoomSnapshot> SetReadyAsync(Guid roomId, string token, bool isReady)
    {
        using var response = await PostAuthorizedAsync(
            $"/api/rooms/{roomId}/ready",
            token,
            new { isReady });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<RoomSnapshot>(await response.Content.ReadFromJsonAsync<RoomSnapshot>());
    }

    private async Task<HttpResponseMessage> PostAuthorizedAsync(string path, string token, object? body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await factory.CreateClient().SendAsync(request);
    }

    private static PlayerSnapshot Member(RoomSnapshot snapshot, Guid playerId) =>
        snapshot.Players.Single(player => player.PlayerId == playerId);

    private static TaskCompletionSource<T> NewCompletion<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource NewCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
