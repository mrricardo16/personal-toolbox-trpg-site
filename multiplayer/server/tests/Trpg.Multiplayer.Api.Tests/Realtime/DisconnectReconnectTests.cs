using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Trpg.Multiplayer.Api.Gameplay;
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

    [Fact]
    public async Task PendingHumanResponse_DisconnectReconnectPreservesExactStateAndRestoresOwnerAffordance()
    {
        var setup = await CreatePendingHumanCombatAsync();
        var store = factory.Services.GetRequiredService<IGameStateStore>();
        Assert.True(store.TryGet(setup.Room.RoomId, out var before));

        await StopAndWaitAsync(setup.DefenderConnection);
        await WaitForCanonicalPlayerAsync(setup.Room.RoomId, setup.Defender.PlayerId, false, 5);
        var defenderRecovered = await AttachForGameAsync(setup.Defender.PlayerSessionToken);
        var attackerRecovered = await AttachForGameAsync(setup.Room.PlayerSessionToken);
        var observerRecovered = await AttachForGameAsync(setup.Observer.PlayerSessionToken);

        Assert.True(store.TryGet(setup.Room.RoomId, out var after));
        AssertCombatStateEqual(before!, after!);
        var pending = Assert.IsType<CombatPendingResponseSnapshot>(
            defenderRecovered.Combat!.ViewerActions!.PendingResponse);
        Assert.Equal(setup.ExchangeId, pending.ExchangeId);
        Assert.Equal(["dodge", "fight_back"], pending.AvailableResponses);
        Assert.NotNull(attackerRecovered.Combat!.Pending);
        Assert.Null(attackerRecovered.Combat.ViewerActions?.PendingResponse);
        Assert.Null(observerRecovered.Combat);
    }

    [Fact]
    public async Task Disconnect_DoesNotResolveRespondPassAdvanceRoundAdvanceTurnOrConsumeDamage()
    {
        var setup = await CreatePendingHumanCombatAsync();
        var store = factory.Services.GetRequiredService<IGameStateStore>();
        Assert.True(store.TryGet(setup.Room.RoomId, out var before));

        await StopAndWaitAsync(setup.DefenderConnection);
        await WaitForCanonicalPlayerAsync(setup.Room.RoomId, setup.Defender.PlayerId, false, 5);

        Assert.True(store.TryGet(setup.Room.RoomId, out var after));
        AssertCombatStateEqual(before!, after!);
        Assert.Equal(setup.ExchangeId, after!.Combat!.PendingExchange!.ExchangeId);
        Assert.Null(after.Combat.LastExchange);
        Assert.Empty(after.Combat.DamageDispositions);
    }

    [Fact]
    public async Task Reconnect_DoesNotMutateGameRevisionOrCanonicalCombat()
    {
        var setup = await CreatePendingHumanCombatAsync();
        var store = factory.Services.GetRequiredService<IGameStateStore>();
        Assert.True(store.TryGet(setup.Room.RoomId, out var before));
        await StopAndWaitAsync(setup.DefenderConnection);
        await WaitForCanonicalPlayerAsync(setup.Room.RoomId, setup.Defender.PlayerId, false, 5);

        var defenderRecovered = await AttachForGameAsync(setup.Defender.PlayerSessionToken);
        var attackerRecovered = await AttachForGameAsync(setup.Room.PlayerSessionToken);
        var observerRecovered = await AttachForGameAsync(setup.Observer.PlayerSessionToken);

        Assert.True(store.TryGet(setup.Room.RoomId, out var after));
        AssertCombatStateEqual(before!, after!);
        Assert.Equal(before!.Revision, defenderRecovered.Revision);
        Assert.Equal(before.Revision, attackerRecovered.Revision);
        Assert.Equal(before.Revision, observerRecovered.Revision);
        Assert.Equal(setup.ExchangeId, defenderRecovered.Combat!.ViewerActions!.PendingResponse!.ExchangeId);
        Assert.Null(attackerRecovered.Combat!.ViewerActions?.PendingResponse);
        Assert.Null(observerRecovered.Combat);
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

    private async Task<GameSnapshot> AttachForGameAsync(string playerSessionToken)
    {
        var connection = CreateHubConnection();
        var gameSnapshot = NewCompletion<GameSnapshot>();
        connection.On<GameSnapshot>("GameSnapshot", snapshot => gameSnapshot.TrySetResult(snapshot));
        await connection.StartAsync();
        await connection.InvokeAsync<RoomSnapshot>("AttachSession", playerSessionToken);
        return await gameSnapshot.Task.WaitAsync(EventTimeout);
    }

    private async Task<PendingHumanCombatSetup> CreatePendingHumanCombatAsync()
    {
        var room = await CreateRoomAsync("Actor");
        var defender = await JoinRoomAsync(room.InviteCode, "Defender");
        var observer = await JoinRoomAsync(room.InviteCode, "Observer");
        using var initialize = await PostAuthorizedAsync(
            $"/api/rooms/{room.RoomId}/game/initialize",
            room.PlayerSessionToken,
            new
            {
                characters = new[]
                {
                    new { playerId = room.PlayerId, name = "Actor", checkValues = CombatValues(80, 80, 40), health = new { currentHp = 12, maxHp = 12, con = 60 } },
                    new { playerId = defender.PlayerId, name = "Defender", checkValues = CombatValues(70, 50, 60), health = new { currentHp = 12, maxHp = 12, con = 60 } }
                }
            });
        Assert.Equal(HttpStatusCode.Created, initialize.StatusCode);
        var initialized = Assert.IsType<GameSnapshot>(await initialize.Content.ReadFromJsonAsync<GameSnapshot>());
        var actorCharacterId = initialized.Characters.Single(character => character.OwnerPlayerId == room.PlayerId).CharacterId;
        var defenderCharacterId = initialized.Characters.Single(character => character.OwnerPlayerId == defender.PlayerId).CharacterId;
        var coordinator = Assert.IsType<GameCoordinator>(factory.Services.GetRequiredService<IGameCoordinator>());
        await InvokeInternalCombatAsync(
            coordinator,
            "StartCombatAsync",
            room.RoomId,
            room.PlayerId,
            1L,
            new[] { actorCharacterId },
            CreateOpponentDefinitions());

        var store = factory.Services.GetRequiredService<IGameStateStore>();
        Assert.True(store.TryGet(room.RoomId, out var started));
        var participants = started!.Combat!.Participants
            .Select(participant => participant.ParticipantId.Value == "opponent:0"
                ? participant with
                {
                    CharacterId = defenderCharacterId,
                    OwnerPlayerId = defender.PlayerId,
                    Kind = "investigator"
                }
                : participant)
            .ToArray();
        var replacement = new MultiplayerGameState(
            started.RoomId,
            started.Revision,
            started.Status,
            started.CreatedAt,
            started.Characters,
            started.LastCheck,
            started.Combat with { Participants = participants });
        Assert.True(store.TryReplace(started, replacement));
        await InvokeInternalCombatAsync(
            coordinator,
            "BeginOpposedExchangeAsync",
            room.RoomId,
            room.PlayerId,
            2L,
            $"character:{actorCharacterId}",
            "opponent:0");
        Assert.True(store.TryGet(room.RoomId, out var pendingState));
        var exchangeId = pendingState!.Combat!.PendingExchange!.ExchangeId;

        var (connection, _) = await AttachAsync(defender.PlayerSessionToken);
        return new PendingHumanCombatSetup(room, defender, observer, connection, exchangeId);
    }

    private static IReadOnlyDictionary<string, int> CombatValues(int dex, int fighting, int dodge) =>
        new Dictionary<string, int>
        {
            ["dex"] = dex,
            ["fighting_brawl"] = fighting,
            ["dodge"] = dodge,
            ["str"] = 60,
            ["siz"] = 50
        };

    private static Array CreateOpponentDefinitions()
    {
        var type = typeof(GameCoordinator).Assembly.GetType("Trpg.Multiplayer.Api.Gameplay.OpponentDefinition")!;
        var definitions = Array.CreateInstance(type, 1);
        definitions.SetValue(
            Activator.CreateInstance(
                type,
                "Defender",
                70,
                50,
                60,
                new[] { CombatResponse.Dodge, CombatResponse.FightBack },
                1,
                CombatResponse.Dodge,
                50,
                50,
                12,
                12,
                0,
                new CombatWeaponProfile(
                    "unarmed",
                    "Unarmed",
                    new DiceExpression("1d3", 1, 3, 0),
                    true,
                    "melee_non_impaling")),
            0);
        return definitions;
    }

    private static async Task InvokeInternalCombatAsync(
        GameCoordinator coordinator,
        string methodName,
        params object?[] arguments)
    {
        var method = typeof(GameCoordinator).GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var command = Activator.CreateInstance(method.GetParameters().Single().ParameterType, arguments)!;
        var task = Assert.IsAssignableFrom<Task>(method.Invoke(coordinator, [command]));
        await task;
        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        Assert.True((bool)result.GetType().GetProperty("IsSuccess")!.GetValue(result)!);
    }

    private static void AssertCombatStateEqual(MultiplayerGameState before, MultiplayerGameState after)
    {
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(before.Combat!.Round, after.Combat!.Round);
        Assert.Equal(before.Combat.TurnIndex, after.Combat.TurnIndex);
        Assert.Equal(before.Combat.PendingExchange, after.Combat.PendingExchange);
        Assert.Equal(before.Combat.DamageDispositions, after.Combat.DamageDispositions);
        Assert.Equal(before.Combat.DyingSchedule, after.Combat.DyingSchedule);
        Assert.Equal(before.Combat.ActionCounts, after.Combat.ActionCounts);
        Assert.Equal(before.Combat.ResponseCounts, after.Combat.ResponseCounts);
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

    private sealed record PendingHumanCombatSetup(
        RoomCreatedResponse Room,
        RoomJoinedResponse Defender,
        RoomJoinedResponse Observer,
        HubConnection DefenderConnection,
        string ExchangeId);
}
