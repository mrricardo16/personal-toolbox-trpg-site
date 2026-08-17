using System.Collections.Concurrent;
using Trpg.Multiplayer.Api.Rooms;
using Xunit;

namespace Trpg.Multiplayer.Api.Tests.Rooms;

public sealed class RoomCoordinatorTests
{
    [Fact]
    public async Task CreateAsync_CreatesLobbyWithHostAndInitialRevision()
    {
        var coordinator = NewCoordinator();
        var hostId = Guid.NewGuid();

        var result = await coordinator.CreateAsync(new CreateRoomCommand(hostId, "Host", 3));

        Assert.True(result.IsSuccess);
        var room = Assert.IsType<RoomSession>(result.Value);
        Assert.NotEqual(Guid.Empty, room.RoomId);
        Assert.Equal(hostId, room.HostPlayerId);
        Assert.Equal(RoomStatus.Lobby, room.Status);
        Assert.Equal(1, room.Revision);
        var host = Assert.Single(room.Players);
        Assert.Equal(hostId, host.PlayerId);
        Assert.True(host.IsHost);
        Assert.False(host.IsReady);
        Assert.False(host.IsConnected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_RejectsBlankHostNickname(string nickname)
    {
        var result = await NewCoordinator().CreateAsync(new CreateRoomCommand(Guid.NewGuid(), nickname, 1));

        AssertError(result, RoomErrorCode.InvalidNickname);
    }

    [Fact]
    public async Task CreateAsync_RejectsNonPositiveCapacity()
    {
        var result = await NewCoordinator().CreateAsync(new CreateRoomCommand(Guid.NewGuid(), "Host", 0));

        AssertError(result, RoomErrorCode.InvalidMaxPlayers);
    }

    [Fact]
    public async Task JoinAsync_AddsPlayerAndIncrementsRevision()
    {
        var coordinator = NewCoordinator();
        var room = await CreateRoomAsync(coordinator, 3);

        var result = await coordinator.JoinAsync(new JoinRoomCommand(room.RoomId, Guid.NewGuid(), "Player"));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Revision);
        Assert.Equal(2, result.Value.Players.Count);
        Assert.False(result.Value.Players.Single(player => !player.IsHost).IsConnected);
    }

    [Fact]
    public async Task JoinAsync_RejectsFullDuplicateClosedAndUnknownRooms()
    {
        var coordinator = NewCoordinator();
        var fullRoom = await CreateRoomAsync(coordinator, 1);
        var duplicateRoom = await CreateRoomAsync(coordinator, 2);
        var duplicatePlayerId = duplicateRoom.HostPlayerId;
        var closingRoom = await CreateRoomAsync(coordinator, 2);
        var hostLeave = await coordinator.LeaveAsync(new LeaveRoomCommand(closingRoom.RoomId, closingRoom.HostPlayerId));

        AssertError(await coordinator.JoinAsync(new JoinRoomCommand(fullRoom.RoomId, Guid.NewGuid(), "Player")), RoomErrorCode.RoomFull);
        AssertError(await coordinator.JoinAsync(new JoinRoomCommand(duplicateRoom.RoomId, duplicatePlayerId, "Again")), RoomErrorCode.PlayerAlreadyExists);
        Assert.True(hostLeave.IsSuccess);
        AssertError(await coordinator.JoinAsync(new JoinRoomCommand(closingRoom.RoomId, Guid.NewGuid(), "Player")), RoomErrorCode.RoomNotFound);
        AssertError(await coordinator.JoinAsync(new JoinRoomCommand(Guid.NewGuid(), Guid.NewGuid(), "Player")), RoomErrorCode.RoomNotFound);
    }

    [Fact]
    public async Task JoinAsync_RejectsBlankNicknameAndClosedRoom()
    {
        var store = new InMemoryRoomStore();
        var room = NewClosedRoom();
        store.TryAdd(room);
        var coordinator = new RoomCoordinator(store);

        AssertError(await coordinator.JoinAsync(new JoinRoomCommand(room.RoomId, Guid.NewGuid(), " ")), RoomErrorCode.InvalidNickname);
        AssertError(await coordinator.JoinAsync(new JoinRoomCommand(room.RoomId, Guid.NewGuid(), "Player")), RoomErrorCode.RoomClosed);
    }

    [Fact]
    public async Task SetReadyAsync_ChangesMemberRevisionAndIsIdempotent()
    {
        var coordinator = NewCoordinator();
        var room = await CreateRoomAsync(coordinator, 2);
        var playerId = Guid.NewGuid();
        await coordinator.JoinAsync(new JoinRoomCommand(room.RoomId, playerId, "Player"));

        var changed = await coordinator.SetReadyAsync(new SetRoomReadyCommand(room.RoomId, playerId, true));
        var repeated = await coordinator.SetReadyAsync(new SetRoomReadyCommand(room.RoomId, playerId, true));

        Assert.True(changed.IsSuccess);
        Assert.True(changed.Changed);
        Assert.Equal(3, changed.Value!.Revision);
        Assert.True(changed.Value.Players.Single(player => player.PlayerId == playerId).IsReady);
        Assert.True(repeated.IsSuccess);
        Assert.False(repeated.Changed);
        Assert.Equal(3, repeated.Value!.Revision);
    }

    [Fact]
    public async Task SetReadyAsync_RejectsNonMemberAndClosedRoom()
    {
        var coordinator = NewCoordinator();
        var room = await CreateRoomAsync(coordinator, 2);

        AssertError(await coordinator.SetReadyAsync(new SetRoomReadyCommand(room.RoomId, Guid.NewGuid(), true)), RoomErrorCode.NotMember);
        var closedStore = new InMemoryRoomStore();
        var closedRoom = NewClosedRoom();
        closedStore.TryAdd(closedRoom);
        var closedCoordinator = new RoomCoordinator(closedStore);
        AssertError(await closedCoordinator.SetReadyAsync(new SetRoomReadyCommand(closedRoom.RoomId, closedRoom.HostPlayerId, true)), RoomErrorCode.RoomClosed);
    }

    [Fact]
    public async Task SetConnectedAsync_ChangesStateAndRevisionInBothDirections()
    {
        var coordinator = NewCoordinator();
        var room = await CreateRoomAsync(coordinator, 2);

        var connected = await coordinator.SetConnectedAsync(
            new SetConnectedRoomCommand(room.RoomId, room.HostPlayerId, true));
        var disconnected = await coordinator.SetConnectedAsync(
            new SetConnectedRoomCommand(room.RoomId, room.HostPlayerId, false));

        Assert.True(connected.IsSuccess);
        Assert.True(connected.Changed);
        Assert.Equal(2, connected.Value!.Revision);
        Assert.True(Assert.Single(connected.Value.Players).IsConnected);
        Assert.True(disconnected.IsSuccess);
        Assert.True(disconnected.Changed);
        Assert.Equal(3, disconnected.Value!.Revision);
        Assert.False(Assert.Single(disconnected.Value.Players).IsConnected);
    }

    [Fact]
    public async Task SetConnectedAsync_SameValueIsIdempotent()
    {
        var coordinator = NewCoordinator();
        var room = await CreateRoomAsync(coordinator, 2);

        var alreadyDisconnected = await coordinator.SetConnectedAsync(
            new SetConnectedRoomCommand(room.RoomId, room.HostPlayerId, false));
        var connected = await coordinator.SetConnectedAsync(
            new SetConnectedRoomCommand(room.RoomId, room.HostPlayerId, true));
        var alreadyConnected = await coordinator.SetConnectedAsync(
            new SetConnectedRoomCommand(room.RoomId, room.HostPlayerId, true));

        Assert.True(alreadyDisconnected.IsSuccess);
        Assert.False(alreadyDisconnected.Changed);
        Assert.Equal(1, alreadyDisconnected.Value!.Revision);
        Assert.True(connected.IsSuccess);
        Assert.True(connected.Changed);
        Assert.Equal(2, connected.Value!.Revision);
        Assert.True(alreadyConnected.IsSuccess);
        Assert.False(alreadyConnected.Changed);
        Assert.Equal(2, alreadyConnected.Value!.Revision);
    }

    [Fact]
    public async Task SetConnectedAsync_RejectsMissingClosedAndNonMemberRooms()
    {
        var coordinator = NewCoordinator();
        var room = await CreateRoomAsync(coordinator, 2);
        var closedStore = new InMemoryRoomStore();
        var closedRoom = NewClosedRoom();
        closedStore.TryAdd(closedRoom);
        var closedCoordinator = new RoomCoordinator(closedStore);

        AssertError(
            await coordinator.SetConnectedAsync(new SetConnectedRoomCommand(Guid.NewGuid(), Guid.NewGuid(), true)),
            RoomErrorCode.RoomNotFound);
        AssertError(
            await closedCoordinator.SetConnectedAsync(
                new SetConnectedRoomCommand(closedRoom.RoomId, closedRoom.HostPlayerId, false)),
            RoomErrorCode.RoomClosed);
        AssertError(
            await coordinator.SetConnectedAsync(new SetConnectedRoomCommand(room.RoomId, Guid.NewGuid(), true)),
            RoomErrorCode.NotMember);
    }

    [Fact]
    public async Task ConcurrentSetConnectedAndLeave_NeverResurrectsRemovedMember()
    {
        var coordinator = NewCoordinator();
        var room = await CreateRoomAsync(coordinator, 2);
        var playerId = Guid.NewGuid();
        var joined = await coordinator.JoinAsync(new JoinRoomCommand(room.RoomId, playerId, "Player"));
        Assert.True(joined.IsSuccess);
        using var startBarrier = new Barrier(participantCount: 3);

        var setConnectedTask = Task.Run(async () =>
        {
            startBarrier.SignalAndWait();
            return await coordinator.SetConnectedAsync(new SetConnectedRoomCommand(room.RoomId, playerId, true));
        });
        var leaveTask = Task.Run(async () =>
        {
            startBarrier.SignalAndWait();
            return await coordinator.LeaveAsync(new LeaveRoomCommand(room.RoomId, playerId));
        });

        startBarrier.SignalAndWait();
        await Task.WhenAll(setConnectedTask, leaveTask);

        var setConnected = await setConnectedTask;
        var leave = await leaveTask;
        Assert.True(leave.IsSuccess);
        Assert.False(leave.Value!.RoomWasClosed);
        Assert.DoesNotContain(leave.Value.Room!.Players, player => player.PlayerId == playerId);
        if (setConnected.IsSuccess)
        {
            Assert.Equal(3, setConnected.Value!.Revision);
            Assert.Equal(4, leave.Value.Room.Revision);
        }
        else
        {
            Assert.Equal(RoomErrorCode.NotMember, setConnected.Error!.Code);
            Assert.Equal(3, leave.Value.Room.Revision);
        }

        var loaded = await coordinator.GetAsync(room.RoomId);
        Assert.True(loaded.IsSuccess);
        Assert.DoesNotContain(loaded.Value!.Players, player => player.PlayerId == playerId);
    }

    [Fact]
    public async Task LeaveAsync_RemovesMemberWithoutAffectingOthersAndIncrementsRevision()
    {
        var coordinator = NewCoordinator();
        var room = await CreateRoomAsync(coordinator, 3);
        var firstPlayerId = Guid.NewGuid();
        var secondPlayerId = Guid.NewGuid();
        await coordinator.JoinAsync(new JoinRoomCommand(room.RoomId, firstPlayerId, "First"));
        await coordinator.JoinAsync(new JoinRoomCommand(room.RoomId, secondPlayerId, "Second"));

        var result = await coordinator.LeaveAsync(new LeaveRoomCommand(room.RoomId, firstPlayerId));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.RoomWasClosed);
        Assert.Equal(4, result.Value.Room!.Revision);
        Assert.DoesNotContain(result.Value.Room.Players, player => player.PlayerId == firstPlayerId);
        Assert.Contains(result.Value.Room.Players, player => player.PlayerId == secondPlayerId);
    }

    [Fact]
    public async Task LeaveAsync_HostClosesAndRemovesRoom()
    {
        var store = new InMemoryRoomStore();
        var coordinator = new RoomCoordinator(store);
        var room = await CreateRoomAsync(coordinator, 2);

        var result = await coordinator.LeaveAsync(new LeaveRoomCommand(room.RoomId, room.HostPlayerId));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.RoomWasClosed);
        Assert.Null(result.Value.Room);
        Assert.False(store.Exists(room.RoomId));
    }

    [Fact]
    public async Task LeaveAsync_RejectsUnknownAndMissingPlayer()
    {
        var coordinator = NewCoordinator();
        var room = await CreateRoomAsync(coordinator, 2);
        var closedStore = new InMemoryRoomStore();
        var closedRoom = NewClosedRoom();
        closedStore.TryAdd(closedRoom);
        var closedCoordinator = new RoomCoordinator(closedStore);

        AssertError(await coordinator.LeaveAsync(new LeaveRoomCommand(Guid.NewGuid(), Guid.NewGuid())), RoomErrorCode.RoomNotFound);
        AssertError(await coordinator.LeaveAsync(new LeaveRoomCommand(room.RoomId, Guid.NewGuid())), RoomErrorCode.PlayerNotFound);
        AssertError(await closedCoordinator.LeaveAsync(new LeaveRoomCommand(closedRoom.RoomId, closedRoom.HostPlayerId)), RoomErrorCode.RoomClosed);
    }

    [Fact]
    public async Task Rooms_AreIsolatedFromEachOther()
    {
        var coordinator = NewCoordinator();
        var roomA = await CreateRoomAsync(coordinator, 2);
        var roomB = await CreateRoomAsync(coordinator, 2);

        await coordinator.JoinAsync(new JoinRoomCommand(roomA.RoomId, Guid.NewGuid(), "Player A"));
        var roomBResult = await coordinator.SetReadyAsync(new SetRoomReadyCommand(roomB.RoomId, roomB.HostPlayerId, true));

        Assert.True(roomBResult.IsSuccess);
        Assert.Equal(2, roomBResult.Value!.Revision);
        var roomAResult = await coordinator.GetAsync(roomA.RoomId);
        Assert.True(roomAResult.IsSuccess);
        Assert.Equal(2, roomAResult.Value!.Revision);
        Assert.False(roomAResult.Value.Players.Single(player => player.PlayerId == roomA.HostPlayerId).IsReady);
    }

    [Fact]
    public async Task ConcurrentJoins_SameRoomNeverExceedsCapacityOrLosesRevision()
    {
        var coordinator = NewCoordinator();
        var room = await CreateRoomAsync(coordinator, 8);
        var results = new ConcurrentBag<RoomResult<RoomSession>>();

        await Task.WhenAll(Enumerable.Range(0, 32).Select(index => Task.Run(async () =>
        {
            var result = await coordinator.JoinAsync(new JoinRoomCommand(room.RoomId, Guid.NewGuid(), $"Player {index}"));
            results.Add(result);
        })));

        Assert.Equal(7, results.Count(result => result.IsSuccess));
        Assert.All(results.Where(result => !result.IsSuccess), result => Assert.Equal(RoomErrorCode.RoomFull, result.Error!.Code));
        var loaded = await coordinator.GetAsync(room.RoomId);
        Assert.True(loaded.IsSuccess);
        Assert.Equal(8, loaded.Value!.Players.Count);
        Assert.Equal(8, loaded.Value.Revision);
        Assert.Equal(8, loaded.Value.Players.Select(player => player.PlayerId).Distinct().Count());
    }

    private static RoomCoordinator NewCoordinator() => new(new InMemoryRoomStore());

    private static RoomSession NewClosedRoom()
    {
        var hostPlayerId = Guid.NewGuid();
        return new RoomSession(
            Guid.NewGuid(),
            hostPlayerId,
            2,
            RoomStatus.Closed,
            1,
            DateTimeOffset.UtcNow,
            [new RoomPlayer(hostPlayerId, "Host", true, false, true)]);
    }

    private static async Task<RoomSession> CreateRoomAsync(RoomCoordinator coordinator, int maxPlayers)
    {
        var result = await coordinator.CreateAsync(new CreateRoomCommand(Guid.NewGuid(), "Host", maxPlayers));
        return Assert.IsType<RoomSession>(result.Value);
    }

    private static void AssertError<T>(RoomResult<T> result, RoomErrorCode expectedCode)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(expectedCode, result.Error!.Code);
    }
}
