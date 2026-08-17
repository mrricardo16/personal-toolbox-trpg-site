using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Trpg.Multiplayer.Api.Realtime;
using Trpg.Multiplayer.Api.Rooms;
using Xunit;

namespace Trpg.Multiplayer.Api.Tests.Realtime;

public sealed class SignalRRoomDeliveryTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IsolationTimeout = TimeSpan.FromSeconds(1);
    private readonly List<HubConnection> connections = [];

    [Fact]
    public async Task AttachSession_WithValidToken_SetsConnectedAndReturnsRoomSnapshotWithoutToken()
    {
        var created = await CreateRoomAsync("Host");
        var connection = CreateHubConnection();
        var connectionChanged = NewCompletion<MemberConnectionChangedEvent>();
        var publishedSnapshot = NewCompletion<RoomSnapshot>();
        connection.On<MemberConnectionChangedEvent>(
            "MemberConnectionChanged",
            message => connectionChanged.TrySetResult(message));
        connection.On<RoomSnapshot>(
            "RoomSnapshot",
            snapshot => publishedSnapshot.TrySetResult(snapshot));

        await connection.StartAsync();
        var snapshot = await connection.InvokeAsync<RoomSnapshot>("AttachSession", created.PlayerSessionToken);
        var changedEvent = await connectionChanged.Task.WaitAsync(EventTimeout);
        var deliveredSnapshot = await publishedSnapshot.Task.WaitAsync(EventTimeout);

        Assert.Equal(created.RoomId, snapshot.RoomId);
        Assert.Equal(2, snapshot.Revision);
        Assert.True(snapshot.Players.Single(player => player.PlayerId == created.PlayerId).IsConnected);
        Assert.Equal(created.RoomId, changedEvent.RoomId);
        Assert.Equal(created.PlayerId, changedEvent.PlayerId);
        Assert.True(changedEvent.IsConnected);
        Assert.Equal(snapshot.Revision, changedEvent.Revision);
        AssertSnapshotsEqual(snapshot, changedEvent.Snapshot);
        AssertSnapshotsEqual(snapshot, deliveredSnapshot);
        var serializedSnapshot = JsonSerializer.Serialize(snapshot);
        Assert.DoesNotContain("PlayerSessionToken", serializedSnapshot, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(created.PlayerSessionToken, serializedSnapshot, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AttachSession_WithInvalidToken_IsRejected()
    {
        var hub = CreateRoomHub(
            new InMemoryPlayerSessionStore(),
            new InMemoryRoomStore(),
            new StubInviteCodeRegistry(Guid.Empty, string.Empty),
            new InMemoryPlayerConnectionRegistry());

        var exception = await Assert.ThrowsAsync<HubException>(() => hub.AttachSession("not-a-valid-token"));

        Assert.Equal("Session attach rejected.", exception.Message);
    }

    [Fact]
    public async Task AttachSession_WhenConnectionIsAlreadyAttachedToAnotherRoom_IsRejected()
    {
        var rooms = new InMemoryRoomStore();
        var sessions = new InMemoryPlayerSessionStore();
        var inviteCodes = new StubInviteCodeRegistry(Guid.Empty, string.Empty);
        var registry = new InMemoryPlayerConnectionRegistry();
        var firstRoomId = Guid.NewGuid();
        var secondRoomId = Guid.NewGuid();
        var firstPlayerId = Guid.NewGuid();
        var secondPlayerId = Guid.NewGuid();
        rooms.TryAdd(CreateRoom(firstRoomId, firstPlayerId, "First Host"));
        rooms.TryAdd(CreateRoom(secondRoomId, secondPlayerId, "Second Host"));
        var firstToken = sessions.Create(firstPlayerId, firstRoomId, true);
        var secondToken = sessions.Create(secondPlayerId, secondRoomId, true);
        var hub = CreateRoomHub(sessions, rooms, inviteCodes, registry);

        await hub.AttachSession(firstToken);

        var exception = await Assert.ThrowsAsync<HubException>(() => hub.AttachSession(secondToken));

        Assert.Equal("Session attach rejected.", exception.Message);
        Assert.Equal(
            ["test-connection"],
            registry.GetConnections(firstRoomId, firstPlayerId));
    }

    [Fact]
    public async Task AttachSession_WithStaleOtherRoomSession_DoesNotCleanupExistingAttachment()
    {
        var rooms = new InMemoryRoomStore();
        var sessions = new InMemoryPlayerSessionStore();
        var inviteCodes = new StubInviteCodeRegistry(Guid.Empty, string.Empty);
        var registry = new InMemoryPlayerConnectionRegistry();
        var attachedRoomId = Guid.NewGuid();
        var staleRoomId = Guid.NewGuid();
        var attachedPlayerId = Guid.NewGuid();
        var stalePlayerId = Guid.NewGuid();
        rooms.TryAdd(CreateRoom(attachedRoomId, attachedPlayerId, "Attached Host"));
        var attachedToken = sessions.Create(attachedPlayerId, attachedRoomId, true);
        var staleToken = sessions.Create(stalePlayerId, staleRoomId, true);
        var hub = CreateRoomHub(sessions, rooms, inviteCodes, registry);
        await hub.AttachSession(attachedToken);

        var exception = await Assert.ThrowsAsync<HubException>(() => hub.AttachSession(staleToken));

        Assert.Equal("Session attach rejected.", exception.Message);
        Assert.Equal(
            ["test-connection"],
            registry.GetConnections(attachedRoomId, attachedPlayerId));
    }

    [Fact]
    public async Task OnDisconnected_WhenGroupRemovalFails_StillClearsCanonicalConnectionState()
    {
        var rooms = new InMemoryRoomStore();
        var sessions = new InMemoryPlayerSessionStore();
        var inviteCodes = new StubInviteCodeRegistry(Guid.Empty, string.Empty);
        var registry = new InMemoryPlayerConnectionRegistry();
        var roomId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        rooms.TryAdd(CreateRoom(roomId, playerId, "Host"));
        var token = sessions.Create(playerId, roomId, true);
        var hub = CreateRoomHub(
            sessions,
            rooms,
            inviteCodes,
            registry,
            new ThrowingRemoveGroupManager());
        var attached = await hub.AttachSession(token);
        Assert.True(Assert.Single(attached.Players).IsConnected);

        await hub.OnDisconnectedAsync(exception: null);

        Assert.Empty(registry.GetRoomConnections(roomId));
        Assert.True(rooms.TryGet(roomId, out var canonical));
        Assert.False(Assert.Single(canonical!.Players).IsConnected);
    }

    [Fact]
    public async Task AttachSession_WhenGroupAddFails_UnregistersConnection()
    {
        var rooms = new InMemoryRoomStore();
        var sessions = new InMemoryPlayerSessionStore();
        var inviteCodes = new StubInviteCodeRegistry(Guid.Empty, string.Empty);
        var registry = new InMemoryPlayerConnectionRegistry();
        var roomId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        rooms.TryAdd(CreateRoom(roomId, playerId, "Host"));
        var token = sessions.Create(playerId, roomId, true);
        var hub = CreateRoomHub(sessions, rooms, inviteCodes, registry, new ThrowingGroupManager());

        await Assert.ThrowsAsync<InvalidOperationException>(() => hub.AttachSession(token));

        Assert.Empty(registry.GetRoomConnections(roomId));
    }

    [Fact]
    public void RoomGroupNames_For_UsesCanonicalRoomGroupName()
    {
        var roomId = Guid.Parse("a7b26370-10cc-43d8-a621-e45be884d136");

        Assert.Equal($"room:{roomId}", RoomGroupNames.For(roomId));
    }

    [Fact]
    public void RoomSnapshotMapper_ToSnapshot_ProjectsCanonicalRoomState()
    {
        var roomId = Guid.NewGuid();
        var hostPlayerId = Guid.NewGuid();
        var room = new RoomSession(
            roomId,
            hostPlayerId,
            4,
            RoomStatus.Lobby,
            7,
            DateTimeOffset.UtcNow,
            [new RoomPlayer(hostPlayerId, "Host", true, true, false)]);
        var inviteCodes = new StubInviteCodeRegistry(roomId, "INVITE1");

        var snapshot = RoomSnapshotMapper.ToSnapshot(room, inviteCodes);

        Assert.Equal(roomId, snapshot.RoomId);
        Assert.Equal("INVITE1", snapshot.InviteCode);
        Assert.Equal(hostPlayerId, snapshot.HostPlayerId);
        Assert.Equal(4, snapshot.MaxPlayers);
        Assert.Equal("Lobby", snapshot.Status);
        Assert.Equal(7, snapshot.Revision);
        var player = Assert.Single(snapshot.Players);
        Assert.Equal(hostPlayerId, player.PlayerId);
        Assert.Equal("Host", player.Nickname);
        Assert.True(player.IsHost);
        Assert.True(player.IsReady);
        Assert.False(player.IsConnected);
    }

    [Fact]
    public void RealtimeEventDtos_WhenSerialized_DoNotContainSessionTokens()
    {
        var roomId = Guid.NewGuid();
        var player = new PlayerSnapshot(Guid.NewGuid(), "Player", false, true, false);
        var snapshot = new RoomSnapshot(roomId, "INVITE1", Guid.NewGuid(), 4, "Waiting", 3, [player]);
        object[] messages =
        [
            new MemberJoinedEvent(roomId, player, snapshot.Revision, snapshot),
            new MemberLeftEvent(roomId, player.PlayerId, snapshot.Revision, snapshot),
            new ReadyChangedEvent(roomId, player.PlayerId, player.IsReady, snapshot.Revision, snapshot),
            new MemberConnectionChangedEvent(
                roomId,
                player.PlayerId,
                player.IsConnected,
                snapshot.Revision,
                snapshot),
            new RoomClosedEvent(roomId)
        ];

        foreach (var message in messages)
        {
            var serializedMessage = JsonSerializer.Serialize(message, message.GetType());
            Assert.DoesNotContain("PlayerSessionToken", serializedMessage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SessionToken", serializedMessage, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Join_BroadcastsMemberJoinedAndSnapshotOnlyToTheJoinedRoom()
    {
        var firstRoom = await CreateRoomAsync("First Host");
        var secondRoom = await CreateRoomAsync("Second Host");
        var firstConnection = await AttachAsync(firstRoom.PlayerSessionToken);
        var secondConnection = await AttachAsync(secondRoom.PlayerSessionToken);
        var firstJoined = NewCompletion<MemberJoinedEvent>();
        var firstSnapshot = NewCompletion<RoomSnapshot>();
        var secondJoined = NewCompletion<MemberJoinedEvent>();
        var crossRoomLeak = NewCompletion<MemberJoinedEvent>();
        var reverseCrossRoomLeak = NewCompletion<MemberJoinedEvent>();

        firstConnection.On<MemberJoinedEvent>("MemberJoined", message => firstJoined.TrySetResult(message));
        firstConnection.On<MemberJoinedEvent>("MemberJoined", message =>
        {
            if (message.RoomId == secondRoom.RoomId)
            {
                reverseCrossRoomLeak.TrySetResult(message);
            }
        });
        firstConnection.On<RoomSnapshot>("RoomSnapshot", snapshot => firstSnapshot.TrySetResult(snapshot));
        secondConnection.On<MemberJoinedEvent>("MemberJoined", message =>
        {
            if (message.RoomId == firstRoom.RoomId)
            {
                crossRoomLeak.TrySetResult(message);
            }
            else if (message.RoomId == secondRoom.RoomId)
            {
                secondJoined.TrySetResult(message);
            }
        });

        var joinedFirstRoom = await JoinRoomAsync(firstRoom.InviteCode, "First Player");
        var joinedEvent = await firstJoined.Task.WaitAsync(EventTimeout);
        var snapshot = await firstSnapshot.Task.WaitAsync(EventTimeout);

        Assert.Equal(firstRoom.RoomId, joinedEvent.RoomId);
        Assert.Equal(joinedFirstRoom.PlayerId, joinedEvent.Player.PlayerId);
        Assert.Equal(3, joinedEvent.Revision);
        Assert.Equal(3, joinedEvent.Snapshot.Revision);
        Assert.Contains(joinedEvent.Snapshot.Players, player => player.PlayerId == joinedFirstRoom.PlayerId);
        AssertSnapshotsEqual(joinedEvent.Snapshot, snapshot);

        await JoinRoomAsync(secondRoom.InviteCode, "Second Player");
        await secondJoined.Task.WaitAsync(EventTimeout);

        await AssertNoEventWithinAsync(crossRoomLeak.Task);
        await AssertNoEventWithinAsync(reverseCrossRoomLeak.Task);
    }

    [Fact]
    public async Task ConcurrentReadyRequests_PublishInAscendingRevisionOrder()
    {
        var notifier = new BlockingReadyNotifier();
        using var isolatedFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRoomRealtimeNotifier>();
                services.AddSingleton<IRoomRealtimeNotifier>(notifier);
            });
        });
        using var client = isolatedFactory.CreateClient();
        var room = await CreateRoomAsync("Host", client);
        var player = await JoinRoomAsync(room.InviteCode, "Player", client);

        var firstResponseTask = PostAuthorizedAsync(
            $"/api/rooms/{room.RoomId}/ready",
            player.PlayerSessionToken,
            new { isReady = true },
            client);
        await notifier.FirstReadyEntered.Task.WaitAsync(EventTimeout);

        var secondResponseTask = PostAuthorizedAsync(
            $"/api/rooms/{room.RoomId}/ready",
            player.PlayerSessionToken,
            new { isReady = false },
            client);
        await CompletesWithinAsync(notifier.SecondReadyEntered.Task, IsolationTimeout);
        notifier.ReleaseFirstReady();

        using var firstResponse = await firstResponseTask.WaitAsync(EventTimeout);
        using var secondResponse = await secondResponseTask.WaitAsync(EventTimeout);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal([3L, 4L], notifier.PublishedReadyRevisions);
    }

    [Fact]
    public async Task NonFirstAttach_WaitsForHttpMutationAndReturnsLatestCanonicalSnapshot()
    {
        var room = await CreateRoomAsync("Host");
        var firstConnection = CreateHubConnection();
        await firstConnection.StartAsync();
        var firstSnapshot = await firstConnection.InvokeAsync<RoomSnapshot>(
            "AttachSession",
            room.PlayerSessionToken);
        Assert.Equal(2, firstSnapshot.Revision);

        var mutationGate = factory.Services.GetRequiredService<RoomMutationDeliveryGate>();
        var coordinator = factory.Services.GetRequiredService<RoomCoordinator>();
        var mutationEntered = NewCompletion();
        var releaseMutation = NewCompletion();
        var mutationTask = mutationGate.RunAsync(room.RoomId, async () =>
        {
            mutationEntered.TrySetResult();
            await releaseMutation.Task;
            return await coordinator.SetReadyAsync(
                new SetRoomReadyCommand(room.RoomId, room.PlayerId, true));
        });
        await mutationEntered.Task.WaitAsync(EventTimeout);

        var secondConnection = CreateHubConnection();
        await secondConnection.StartAsync();
        var attachTask = secondConnection.InvokeAsync<RoomSnapshot>(
            "AttachSession",
            room.PlayerSessionToken);

        bool attachCompletedBeforeMutationRelease;
        try
        {
            attachCompletedBeforeMutationRelease = await CompletesWithinAsync(
                attachTask,
                IsolationTimeout);
        }
        finally
        {
            releaseMutation.TrySetResult();
        }

        var mutation = await mutationTask.WaitAsync(EventTimeout);
        var attached = await attachTask.WaitAsync(EventTimeout);

        Assert.False(attachCompletedBeforeMutationRelease);
        Assert.True(mutation.IsSuccess);
        Assert.Equal(mutation.Value!.Revision, attached.Revision);
        var host = Assert.Single(attached.Players);
        Assert.True(host.IsConnected);
        Assert.True(host.IsReady);
    }

    [Fact]
    public async Task LastDisconnect_HoldsLifecycleGateThroughNotifierBeforeFirstReattach()
    {
        var notifier = new BlockingConnectionNotifier();
        using var isolatedFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRoomRealtimeNotifier>();
                services.AddSingleton(notifier);
                services.AddSingleton<IRoomRealtimeNotifier>(notifier);
            });
        });
        using var client = isolatedFactory.CreateClient();
        var room = await CreateRoomAsync("Host", client);
        var firstConnection = CreateHubConnection(isolatedFactory);
        await firstConnection.StartAsync();
        await firstConnection.InvokeAsync<RoomSnapshot>("AttachSession", room.PlayerSessionToken);

        var disconnectTask = firstConnection.StopAsync();
        await notifier.DisconnectedEntered.Task.WaitAsync(EventTimeout);

        var secondConnection = CreateHubConnection(isolatedFactory);
        await secondConnection.StartAsync();
        var attachTask = secondConnection.InvokeAsync<RoomSnapshot>(
            "AttachSession",
            room.PlayerSessionToken);

        bool attachCompletedBeforeDisconnectDelivery;
        try
        {
            attachCompletedBeforeDisconnectDelivery = await CompletesWithinAsync(
                attachTask,
                IsolationTimeout);
        }
        finally
        {
            notifier.ReleaseDisconnect();
        }

        await disconnectTask.WaitAsync(EventTimeout);
        var attached = await attachTask.WaitAsync(EventTimeout);

        Assert.False(attachCompletedBeforeDisconnectDelivery);
        Assert.Equal([2L, 3L, 4L], notifier.PublishedRevisions);
        Assert.True(attached.Players.Single(player => player.PlayerId == room.PlayerId).IsConnected);
        var registry = isolatedFactory.Services.GetRequiredService<IPlayerConnectionRegistry>();
        Assert.Equal(
            [secondConnection.ConnectionId!],
            registry.GetConnections(room.RoomId, room.PlayerId));
    }

    [Fact]
    public async Task AttachRacingHostClose_WaitsForCleanupAndLeavesNoRegistryOrGroupMembership()
    {
        using var isolatedFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRoomRealtimeNotifier>();
                services.AddSingleton<SignalRRoomRealtimeNotifier>();
                services.AddSingleton<BlockingRoomClosedNotifier>();
                services.AddSingleton<IRoomRealtimeNotifier>(serviceProvider =>
                    serviceProvider.GetRequiredService<BlockingRoomClosedNotifier>());
            });
        });
        using var client = isolatedFactory.CreateClient();
        var room = await CreateRoomAsync("Host", client);
        var player = await JoinRoomAsync(room.InviteCode, "Player", client);
        var hostConnection = CreateHubConnection(isolatedFactory);
        var staleGroupEvent = NewCompletion<ReadyChangedEvent>();
        hostConnection.On<ReadyChangedEvent>(
            "ReadyChanged",
            message => staleGroupEvent.TrySetResult(message));
        await hostConnection.StartAsync();
        await hostConnection.InvokeAsync<RoomSnapshot>("AttachSession", room.PlayerSessionToken);
        var notifier = isolatedFactory.Services.GetRequiredService<BlockingRoomClosedNotifier>();

        var leaveTask = PostAuthorizedAsync(
            $"/api/rooms/{room.RoomId}/leave",
            room.PlayerSessionToken,
            body: null,
            client);
        await notifier.RoomClosedEntered.Task.WaitAsync(EventTimeout);

        var racingConnection = CreateHubConnection(isolatedFactory);
        await racingConnection.StartAsync();
        var attachTask = racingConnection.InvokeAsync<RoomSnapshot>(
            "AttachSession",
            player.PlayerSessionToken);

        bool attachCompletedBeforeCloseCleanup;
        try
        {
            attachCompletedBeforeCloseCleanup = await SettlesWithinAsync(
                attachTask,
                IsolationTimeout);
        }
        finally
        {
            notifier.ReleaseRoomClosed();
        }

        using var leaveResponse = await leaveTask.WaitAsync(EventTimeout);
        Assert.Equal(HttpStatusCode.OK, leaveResponse.StatusCode);
        await Assert.ThrowsAsync<HubException>(() => attachTask);

        Assert.False(attachCompletedBeforeCloseCleanup);
        var registry = isolatedFactory.Services.GetRequiredService<IPlayerConnectionRegistry>();
        Assert.Empty(registry.GetRoomConnections(room.RoomId));

        var closedSnapshot = new RoomSnapshot(
            room.RoomId,
            room.InviteCode,
            room.PlayerId,
            4,
            "Closed",
            4,
            []);
        await notifier.PublishReadyChangedAsync(closedSnapshot, room.PlayerId, true);
        await AssertNoEventWithinAsync(staleGroupEvent.Task);
    }

    [Fact]
    public async Task Ready_BroadcastsOnlyChangedResultsWithAuthoritativeSnapshots()
    {
        var room = await CreateRoomAsync("Host");
        var player = await JoinRoomAsync(room.InviteCode, "Player");
        var connection = await AttachAsync(room.PlayerSessionToken);
        var readyTrue = NewCompletion<ReadyChangedEvent>();
        var readyFalse = NewCompletion<ReadyChangedEvent>();
        var trueSnapshot = NewCompletion<RoomSnapshot>();
        var falseSnapshot = NewCompletion<RoomSnapshot>();
        var eventCount = 0;

        connection.On<ReadyChangedEvent>("ReadyChanged", message =>
        {
            Interlocked.Increment(ref eventCount);
            (message.IsReady ? readyTrue : readyFalse).TrySetResult(message);
        });
        connection.On<RoomSnapshot>("RoomSnapshot", snapshot =>
        {
            var changedPlayer = snapshot.Players.Single(candidate => candidate.PlayerId == player.PlayerId);
            (changedPlayer.IsReady ? trueSnapshot : falseSnapshot).TrySetResult(snapshot);
        });

        var changedResponse = await PostAuthorizedAsync(
            $"/api/rooms/{room.RoomId}/ready",
            player.PlayerSessionToken,
            new { isReady = true });
        Assert.Equal(HttpStatusCode.OK, changedResponse.StatusCode);
        var changedEvent = await readyTrue.Task.WaitAsync(EventTimeout);
        var changedSnapshot = await trueSnapshot.Task.WaitAsync(EventTimeout);

        var repeatedResponse = await PostAuthorizedAsync(
            $"/api/rooms/{room.RoomId}/ready",
            player.PlayerSessionToken,
            new { isReady = true });
        Assert.Equal(HttpStatusCode.OK, repeatedResponse.StatusCode);

        var resetResponse = await PostAuthorizedAsync(
            $"/api/rooms/{room.RoomId}/ready",
            player.PlayerSessionToken,
            new { isReady = false });
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);
        var resetEvent = await readyFalse.Task.WaitAsync(EventTimeout);
        var resetSnapshot = await falseSnapshot.Task.WaitAsync(EventTimeout);

        Assert.Equal(player.PlayerId, changedEvent.PlayerId);
        Assert.True(changedEvent.IsReady);
        Assert.Equal(4, changedEvent.Revision);
        AssertSnapshotsEqual(changedEvent.Snapshot, changedSnapshot);
        Assert.False(resetEvent.IsReady);
        Assert.Equal(5, resetEvent.Revision);
        AssertSnapshotsEqual(resetEvent.Snapshot, resetSnapshot);
        Assert.Equal(2, Volatile.Read(ref eventCount));
    }

    [Fact]
    public async Task NormalLeave_RemovesLeavingConnectionBeforeBroadcastingToRemainingMembers()
    {
        var room = await CreateRoomAsync("Host");
        var player = await JoinRoomAsync(room.InviteCode, "Player");
        var hostConnection = await AttachAsync(room.PlayerSessionToken);
        var playerConnection = await AttachAsync(player.PlayerSessionToken);
        var memberLeft = NewCompletion<MemberLeftEvent>();
        var leaveSnapshot = NewCompletion<RoomSnapshot>();
        var hostReadyBarrier = NewCompletion<ReadyChangedEvent>();
        var leavingConnectionEvent = NewCompletion<ReadyChangedEvent>();

        hostConnection.On<MemberLeftEvent>("MemberLeft", message => memberLeft.TrySetResult(message));
        hostConnection.On<RoomSnapshot>("RoomSnapshot", snapshot =>
        {
            if (snapshot.Players.All(candidate => candidate.PlayerId != player.PlayerId))
            {
                leaveSnapshot.TrySetResult(snapshot);
            }
        });
        hostConnection.On<ReadyChangedEvent>("ReadyChanged", message => hostReadyBarrier.TrySetResult(message));
        playerConnection.On<ReadyChangedEvent>("ReadyChanged", message => leavingConnectionEvent.TrySetResult(message));

        var leaveResponse = await PostAuthorizedAsync(
            $"/api/rooms/{room.RoomId}/leave",
            player.PlayerSessionToken,
            body: null);
        Assert.Equal(HttpStatusCode.OK, leaveResponse.StatusCode);
        var leftEvent = await memberLeft.Task.WaitAsync(EventTimeout);
        var snapshot = await leaveSnapshot.Task.WaitAsync(EventTimeout);

        Assert.Equal(player.PlayerId, leftEvent.PlayerId);
        Assert.Equal(5, leftEvent.Revision);
        Assert.DoesNotContain(leftEvent.Snapshot.Players, candidate => candidate.PlayerId == player.PlayerId);
        AssertSnapshotsEqual(leftEvent.Snapshot, snapshot);

        var readyResponse = await PostAuthorizedAsync(
            $"/api/rooms/{room.RoomId}/ready",
            room.PlayerSessionToken,
            new { isReady = true });
        Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);
        await hostReadyBarrier.Task.WaitAsync(EventTimeout);

        await AssertNoEventWithinAsync(leavingConnectionEvent.Task);
    }

    [Fact]
    public async Task NormalLeave_WhenMemberLeftDeliveryFails_RevokesSessionAndPropagatesFailure()
    {
        using var isolatedFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRoomRealtimeNotifier>();
                services.AddSingleton<IRoomRealtimeNotifier, ThrowingMemberLeftNotifier>();
            });
        });
        using var client = isolatedFactory.CreateClient();
        var room = await CreateRoomAsync("Host", client);
        var player = await JoinRoomAsync(room.InviteCode, "Player", client);

        var leaveResponse = await PostAuthorizedAsync(
            $"/api/rooms/{room.RoomId}/leave",
            player.PlayerSessionToken,
            body: null,
            client);

        Assert.Equal(HttpStatusCode.InternalServerError, leaveResponse.StatusCode);

        var staleSessionResponse = await PostAuthorizedAsync(
            $"/api/rooms/{room.RoomId}/ready",
            player.PlayerSessionToken,
            new { isReady = true },
            client);
        Assert.Equal(HttpStatusCode.Unauthorized, staleSessionResponse.StatusCode);
    }

    [Fact]
    public async Task HostLeave_BroadcastsRoomClosedToAllAttachedMembers()
    {
        var room = await CreateRoomAsync("Host");
        var player = await JoinRoomAsync(room.InviteCode, "Player");
        var hostConnection = await AttachAsync(room.PlayerSessionToken);
        var playerConnection = await AttachAsync(player.PlayerSessionToken);
        var hostClosed = NewCompletion<RoomClosedEvent>();
        var playerClosed = NewCompletion<RoomClosedEvent>();

        hostConnection.On<RoomClosedEvent>("RoomClosed", message => hostClosed.TrySetResult(message));
        playerConnection.On<RoomClosedEvent>("RoomClosed", message => playerClosed.TrySetResult(message));

        var response = await PostAuthorizedAsync(
            $"/api/rooms/{room.RoomId}/leave",
            room.PlayerSessionToken,
            body: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(room.RoomId, (await hostClosed.Task.WaitAsync(EventTimeout)).RoomId);
        Assert.Equal(room.RoomId, (await playerClosed.Task.WaitAsync(EventTimeout)).RoomId);

        var registry = factory.Services.GetRequiredService<IPlayerConnectionRegistry>();
        Assert.Empty(registry.GetRoomConnections(room.RoomId));

        var staleConnection = CreateHubConnection();
        await staleConnection.StartAsync();
        await Assert.ThrowsAsync<HubException>(
            () => staleConnection.InvokeAsync<RoomSnapshot>("AttachSession", room.PlayerSessionToken));
    }

    [Fact]
    public async Task PublishRoomClosed_WhenDeliveryFails_StillCleansGroupAndRegistry()
    {
        var roomId = Guid.NewGuid();
        var registry = new InMemoryPlayerConnectionRegistry();
        registry.Register("connection-1", roomId, Guid.NewGuid());
        registry.Register("connection-2", roomId, Guid.NewGuid());
        var groups = new RecordingGroupManager();
        var hubContext = new StubRoomHubContext(
            new StubHubClients(new ThrowingRoomClosedClient()),
            groups);
        var notifier = new SignalRRoomRealtimeNotifier(hubContext, registry);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => notifier.PublishRoomClosedAsync(roomId));

        Assert.Empty(registry.GetRoomConnections(roomId));
        Assert.Equal(
            ["connection-1", "connection-2"],
            groups.RemovedConnectionIds.OrderBy(connectionId => connectionId));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var connection in connections)
        {
            await connection.DisposeAsync();
        }
    }

    private HubConnection CreateHubConnection(WebApplicationFactory<Program>? appFactory = null)
    {
        var server = (appFactory ?? factory).Server;
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

    private async Task<HubConnection> AttachAsync(string playerSessionToken)
    {
        var connection = CreateHubConnection();
        await connection.StartAsync();
        await connection.InvokeAsync<RoomSnapshot>("AttachSession", playerSessionToken);
        return connection;
    }

    private async Task<RoomCreatedResponse> CreateRoomAsync(string nickname, HttpClient? client = null)
    {
        var response = await (client ?? factory.CreateClient()).PostAsJsonAsync(
            "/api/rooms",
            new { nickname, maxPlayers = 4 });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<RoomCreatedResponse>(
            await response.Content.ReadFromJsonAsync<RoomCreatedResponse>());
    }

    private async Task<RoomJoinedResponse> JoinRoomAsync(
        string inviteCode,
        string nickname,
        HttpClient? client = null)
    {
        var response = await (client ?? factory.CreateClient()).PostAsJsonAsync(
            "/api/rooms/join",
            new { inviteCode, nickname });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<RoomJoinedResponse>(
            await response.Content.ReadFromJsonAsync<RoomJoinedResponse>());
    }

    private async Task<HttpResponseMessage> PostAuthorizedAsync(
        string path,
        string token,
        object? body,
        HttpClient? client = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await (client ?? factory.CreateClient()).SendAsync(request);
    }

    private static TaskCompletionSource<T> NewCompletion<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource NewCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task AssertNoEventWithinAsync<T>(Task<T> eventTask)
    {
        await Assert.ThrowsAsync<TimeoutException>(() => eventTask.WaitAsync(IsolationTimeout));
    }

    private static async Task<bool> CompletesWithinAsync(Task task, TimeSpan timeout)
    {
        try
        {
            await task.WaitAsync(timeout);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static async Task<bool> SettlesWithinAsync(Task task, TimeSpan timeout)
    {
        try
        {
            await task.WaitAsync(timeout);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static void AssertSnapshotsEqual(RoomSnapshot expected, RoomSnapshot actual)
    {
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    }

    private static RoomHub CreateRoomHub(
        IPlayerSessionStore sessions,
        IRoomStore rooms,
        IInviteCodeRegistry inviteCodes,
        IPlayerConnectionRegistry registry,
        IGroupManager? groups = null)
    {
        return new RoomHub(
            sessions,
            rooms,
            inviteCodes,
            registry,
            new RoomCoordinator(rooms),
            new NoOpRoomRealtimeNotifier(),
            new RoomMutationDeliveryGate(),
            NullLogger<RoomHub>.Instance)
        {
            Context = new TestHubCallerContext("test-connection"),
            Groups = groups ?? new NoOpGroupManager()
        };
    }

    private static RoomSession CreateRoom(Guid roomId, Guid playerId, string nickname)
    {
        return new RoomSession(
            roomId,
            playerId,
            4,
            RoomStatus.Lobby,
            1,
            DateTimeOffset.UtcNow,
            [new RoomPlayer(playerId, nickname, true, false, false)]);
    }

    private sealed class TestHubCallerContext(string connectionId) : HubCallerContext
    {
        public override string ConnectionId => connectionId;

        public override string? UserIdentifier => null;

        public override ClaimsPrincipal? User => null;

        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();

        public override IFeatureCollection Features { get; } = new FeatureCollection();

        public override CancellationToken ConnectionAborted => CancellationToken.None;

        public override void Abort()
        {
        }
    }

    private sealed class NoOpGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ThrowingGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Group add failed.");

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ThrowingRemoveGroupManager : IGroupManager
    {
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Group remove failed.");
    }

    private sealed class RecordingGroupManager : IGroupManager
    {
        private readonly ConcurrentQueue<string> removedConnectionIds = new();

        public IReadOnlyCollection<string> RemovedConnectionIds => removedConnectionIds.ToArray();

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            removedConnectionIds.Enqueue(connectionId);
            return Task.CompletedTask;
        }
    }

    private sealed class StubRoomHubContext(
        IHubClients<IRoomClient> clients,
        IGroupManager groups) : IHubContext<RoomHub, IRoomClient>
    {
        public IHubClients<IRoomClient> Clients { get; } = clients;

        public IGroupManager Groups { get; } = groups;
    }

    private sealed class StubHubClients(IRoomClient roomClient) : IHubClients<IRoomClient>
    {
        public IRoomClient All => roomClient;

        public IRoomClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => roomClient;

        public IRoomClient Client(string connectionId) => roomClient;

        public IRoomClient Clients(IReadOnlyList<string> connectionIds) => roomClient;

        public IRoomClient Group(string groupName) => roomClient;

        public IRoomClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => roomClient;

        public IRoomClient Groups(IReadOnlyList<string> groupNames) => roomClient;

        public IRoomClient User(string userId) => roomClient;

        public IRoomClient Users(IReadOnlyList<string> userIds) => roomClient;
    }

    private sealed class ThrowingRoomClosedClient : IRoomClient
    {
        public Task RoomSnapshot(RoomSnapshot snapshot) => Task.CompletedTask;

        public Task MemberJoined(MemberJoinedEvent message) => Task.CompletedTask;

        public Task MemberLeft(MemberLeftEvent message) => Task.CompletedTask;

        public Task ReadyChanged(ReadyChangedEvent message) => Task.CompletedTask;

        public Task MemberConnectionChanged(MemberConnectionChangedEvent message) => Task.CompletedTask;

        public Task RoomClosed(RoomClosedEvent message) =>
            throw new InvalidOperationException("RoomClosed delivery failed.");
    }

    private sealed class BlockingReadyNotifier : IRoomRealtimeNotifier
    {
        private readonly ConcurrentQueue<long> publishedReadyRevisions = new();
        private readonly TaskCompletionSource releaseFirstReady = NewCompletion();
        private int readyPublishCount;

        public TaskCompletionSource FirstReadyEntered { get; } = NewCompletion();

        public TaskCompletionSource SecondReadyEntered { get; } = NewCompletion();

        public IReadOnlyCollection<long> PublishedReadyRevisions => publishedReadyRevisions.ToArray();

        public Task PublishRoomSnapshotAsync(RoomSnapshot snapshot) => Task.CompletedTask;

        public Task PublishMemberJoinedAsync(RoomSnapshot snapshot, PlayerSnapshot player) => Task.CompletedTask;

        public async Task PublishReadyChangedAsync(RoomSnapshot snapshot, Guid playerId, bool isReady)
        {
            if (Interlocked.Increment(ref readyPublishCount) == 1)
            {
                FirstReadyEntered.TrySetResult();
                await releaseFirstReady.Task;
            }
            else
            {
                SecondReadyEntered.TrySetResult();
            }

            publishedReadyRevisions.Enqueue(snapshot.Revision);
        }

        public Task PublishMemberLeftAsync(RoomSnapshot snapshot, Guid playerId) => Task.CompletedTask;

        public Task PublishMemberConnectionChangedAsync(RoomSnapshot snapshot, Guid playerId, bool isConnected) =>
            Task.CompletedTask;

        public Task PublishRoomClosedAsync(Guid roomId) => Task.CompletedTask;

        public void ReleaseFirstReady() => releaseFirstReady.TrySetResult();

        private static TaskCompletionSource NewCompletion() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class BlockingConnectionNotifier : IRoomRealtimeNotifier
    {
        private readonly ConcurrentQueue<long> publishedRevisions = new();
        private readonly TaskCompletionSource releaseDisconnect = NewCompletion();

        public TaskCompletionSource DisconnectedEntered { get; } = NewCompletion();

        public IReadOnlyCollection<long> PublishedRevisions => publishedRevisions.ToArray();

        public Task PublishRoomSnapshotAsync(RoomSnapshot snapshot) => Task.CompletedTask;

        public Task PublishMemberJoinedAsync(RoomSnapshot snapshot, PlayerSnapshot player) => Task.CompletedTask;

        public Task PublishReadyChangedAsync(RoomSnapshot snapshot, Guid playerId, bool isReady) => Task.CompletedTask;

        public Task PublishMemberLeftAsync(RoomSnapshot snapshot, Guid playerId) => Task.CompletedTask;

        public async Task PublishMemberConnectionChangedAsync(
            RoomSnapshot snapshot,
            Guid playerId,
            bool isConnected)
        {
            if (!isConnected)
            {
                DisconnectedEntered.TrySetResult();
                await releaseDisconnect.Task;
            }

            publishedRevisions.Enqueue(snapshot.Revision);
        }

        public Task PublishRoomClosedAsync(Guid roomId) => Task.CompletedTask;

        public void ReleaseDisconnect() => releaseDisconnect.TrySetResult();
    }

    private sealed class BlockingRoomClosedNotifier(SignalRRoomRealtimeNotifier inner)
        : IRoomRealtimeNotifier
    {
        private readonly TaskCompletionSource releaseRoomClosed = NewCompletion();

        public TaskCompletionSource RoomClosedEntered { get; } = NewCompletion();

        public Task PublishRoomSnapshotAsync(RoomSnapshot snapshot) =>
            inner.PublishRoomSnapshotAsync(snapshot);

        public Task PublishMemberJoinedAsync(RoomSnapshot snapshot, PlayerSnapshot player) =>
            inner.PublishMemberJoinedAsync(snapshot, player);

        public Task PublishReadyChangedAsync(RoomSnapshot snapshot, Guid playerId, bool isReady) =>
            inner.PublishReadyChangedAsync(snapshot, playerId, isReady);

        public Task PublishMemberLeftAsync(RoomSnapshot snapshot, Guid playerId) =>
            inner.PublishMemberLeftAsync(snapshot, playerId);

        public Task PublishMemberConnectionChangedAsync(
            RoomSnapshot snapshot,
            Guid playerId,
            bool isConnected) =>
            inner.PublishMemberConnectionChangedAsync(snapshot, playerId, isConnected);

        public async Task PublishRoomClosedAsync(Guid roomId)
        {
            RoomClosedEntered.TrySetResult();
            await releaseRoomClosed.Task;
            await inner.PublishRoomClosedAsync(roomId);
        }

        public void ReleaseRoomClosed() => releaseRoomClosed.TrySetResult();
    }

    private sealed class ThrowingMemberLeftNotifier : IRoomRealtimeNotifier
    {
        public Task PublishRoomSnapshotAsync(RoomSnapshot snapshot) => Task.CompletedTask;

        public Task PublishMemberJoinedAsync(RoomSnapshot snapshot, PlayerSnapshot player) => Task.CompletedTask;

        public Task PublishReadyChangedAsync(RoomSnapshot snapshot, Guid playerId, bool isReady) => Task.CompletedTask;

        public Task PublishMemberLeftAsync(RoomSnapshot snapshot, Guid playerId) =>
            throw new InvalidOperationException("MemberLeft delivery failed.");

        public Task PublishMemberConnectionChangedAsync(RoomSnapshot snapshot, Guid playerId, bool isConnected) =>
            Task.CompletedTask;

        public Task PublishRoomClosedAsync(Guid roomId) => Task.CompletedTask;
    }

    private sealed class NoOpRoomRealtimeNotifier : IRoomRealtimeNotifier
    {
        public Task PublishRoomSnapshotAsync(RoomSnapshot snapshot) => Task.CompletedTask;

        public Task PublishMemberJoinedAsync(RoomSnapshot snapshot, PlayerSnapshot player) => Task.CompletedTask;

        public Task PublishReadyChangedAsync(RoomSnapshot snapshot, Guid playerId, bool isReady) => Task.CompletedTask;

        public Task PublishMemberLeftAsync(RoomSnapshot snapshot, Guid playerId) => Task.CompletedTask;

        public Task PublishMemberConnectionChangedAsync(RoomSnapshot snapshot, Guid playerId, bool isConnected) =>
            Task.CompletedTask;

        public Task PublishRoomClosedAsync(Guid roomId) => Task.CompletedTask;
    }

    private sealed class StubInviteCodeRegistry(Guid roomId, string inviteCode) : IInviteCodeRegistry
    {
        public bool TryRegister(Guid candidateRoomId, out string registeredInviteCode)
        {
            registeredInviteCode = string.Empty;
            return false;
        }

        public bool TryGetRoomId(string candidateInviteCode, out Guid registeredRoomId)
        {
            registeredRoomId = Guid.Empty;
            return false;
        }

        public bool TryGetInviteCode(Guid candidateRoomId, out string? registeredInviteCode)
        {
            registeredInviteCode = candidateRoomId == roomId ? inviteCode : null;
            return registeredInviteCode is not null;
        }

        public void Remove(Guid candidateRoomId)
        {
        }
    }
}
