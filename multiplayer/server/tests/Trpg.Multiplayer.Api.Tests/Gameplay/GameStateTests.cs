using System.Text.Json;
using Trpg.Multiplayer.Api.Gameplay;
using Trpg.Multiplayer.Api.Rooms;
using Xunit;

namespace Trpg.Multiplayer.Api.Tests.Gameplay;

public sealed class GameStateTests
{
    [Fact]
    public void InMemoryGameStateStore_SupportsCreateGetReplaceRemoveAndIsolation()
    {
        var store = new InMemoryGameStateStore();
        var first = CreateState(Guid.NewGuid(), Guid.NewGuid(), "First");
        var second = CreateState(Guid.NewGuid(), Guid.NewGuid(), "Second");
        var replacement = new MultiplayerGameState(
            first.RoomId,
            2,
            first.Status,
            first.CreatedAt,
            first.Characters);

        Assert.True(store.TryAdd(first));
        Assert.True(store.TryAdd(second));
        Assert.False(store.TryAdd(first));
        Assert.True(store.Exists(first.RoomId));
        Assert.True(store.TryGet(first.RoomId, out var loaded));
        Assert.Same(first, loaded);
        Assert.False(store.TryGet(Guid.NewGuid(), out _));
        Assert.True(store.TryReplace(first, replacement));
        Assert.False(store.TryReplace(first, first));
        Assert.True(store.TryGet(first.RoomId, out loaded));
        Assert.Same(replacement, loaded);
        Assert.True(store.TryGet(second.RoomId, out var other));
        Assert.Same(second, other);
        Assert.True(store.TryRemove(first.RoomId, out var removed));
        Assert.Same(replacement, removed);
        Assert.False(store.Exists(first.RoomId));
        Assert.True(store.Exists(second.RoomId));
    }

    [Fact]
    public async Task Initialize_MapsRoomMembersToServerGeneratedCharacters()
    {
        var roomStore = new InMemoryRoomStore();
        var hostId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var room = CreateRoom(roomStore, hostId, "Host");
        Assert.True((await new RoomCoordinator(roomStore).JoinAsync(new JoinRoomCommand(room.RoomId, memberId, "Member"))).IsSuccess);
        var coordinator = new GameCoordinator(roomStore, new InMemoryGameStateStore());

        var result = await coordinator.InitializeAsync(new InitializeGameCommand(
            room.RoomId,
            hostId,
            [
                new InitializeCharacterCommand(hostId, "Investigator", new Dictionary<string, int> { ["spotHidden"] = 60 }),
                new InitializeCharacterCommand(memberId, "Archivist", new Dictionary<string, int> { ["spotHidden"] = 40 })
            ]));

        Assert.True(result.IsSuccess);
        var state = Assert.IsType<MultiplayerGameState>(result.Value);
        Assert.Equal(room.RoomId, state.RoomId);
        Assert.Equal(1, state.Revision);
        Assert.Equal(MultiplayerGameStatus.Active, state.Status);
        Assert.Equal(2, state.Characters.Count);
        Assert.All(state.Characters, character => Assert.NotEqual(Guid.Empty, character.CharacterId));
        Assert.Equal("Archivist", state.Characters.Single(character => character.OwnerPlayerId == memberId).Name);
    }

    [Fact]
    public async Task Initialize_RejectsUnknownAndDuplicateOwnersAndNonHost()
    {
        var roomStore = new InMemoryRoomStore();
        var hostId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var unknownId = Guid.NewGuid();
        var room = CreateRoom(roomStore, hostId, "Host");
        Assert.True((await new RoomCoordinator(roomStore).JoinAsync(new JoinRoomCommand(room.RoomId, memberId, "Member"))).IsSuccess);
        var coordinator = new GameCoordinator(roomStore, new InMemoryGameStateStore());

        var unknown = await coordinator.InitializeAsync(new InitializeGameCommand(
            room.RoomId,
            hostId,
            [new InitializeCharacterCommand(unknownId, "Unknown", Values())]));
        Assert.Equal(GameErrorCode.UnknownPlayer, unknown.Error?.Code);

        var duplicate = await coordinator.InitializeAsync(new InitializeGameCommand(
            room.RoomId,
            hostId,
            [
                new InitializeCharacterCommand(memberId, "One", Values()),
                new InitializeCharacterCommand(memberId, "Two", Values())
            ]));
        Assert.Equal(GameErrorCode.DuplicateCharacterOwnership, duplicate.Error?.Code);

        var notHost = await coordinator.InitializeAsync(new InitializeGameCommand(
            room.RoomId,
            memberId,
            [new InitializeCharacterCommand(memberId, "Member", Values())]));
        Assert.Equal(GameErrorCode.NotHost, notHost.Error?.Code);
    }

    [Fact]
    public async Task Initialize_IsIdempotentlyRejectedAndDifferentRoomsCanInitializeConcurrently()
    {
        var roomStore = new InMemoryRoomStore();
        var firstHost = Guid.NewGuid();
        var secondHost = Guid.NewGuid();
        var firstRoom = CreateRoom(roomStore, firstHost, "First");
        var secondRoom = CreateRoom(roomStore, secondHost, "Second");
        var coordinator = new GameCoordinator(roomStore, new InMemoryGameStateStore());

        var results = await Task.WhenAll(
            coordinator.InitializeAsync(new InitializeGameCommand(firstRoom.RoomId, firstHost, [new InitializeCharacterCommand(firstHost, "One", Values())])),
            coordinator.InitializeAsync(new InitializeGameCommand(secondRoom.RoomId, secondHost, [new InitializeCharacterCommand(secondHost, "Two", Values())])));

        Assert.All(results, result => Assert.True(result.IsSuccess));
        var duplicate = await coordinator.InitializeAsync(new InitializeGameCommand(
            firstRoom.RoomId,
            firstHost,
            [new InitializeCharacterCommand(firstHost, "Overwrite", Values())]));
        Assert.Equal(GameErrorCode.AlreadyInitialized, duplicate.Error?.Code);
    }

    [Fact]
    public async Task Projection_IsSeparatePlayerSafeDtoAndOwnershipIsEnforced()
    {
        var roomStore = new InMemoryRoomStore();
        var hostId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var room = CreateRoom(roomStore, hostId, "Host");
        Assert.True((await new RoomCoordinator(roomStore).JoinAsync(new JoinRoomCommand(room.RoomId, memberId, "Member"))).IsSuccess);
        var coordinator = new GameCoordinator(roomStore, new InMemoryGameStateStore());
        Assert.True((await coordinator.InitializeAsync(new InitializeGameCommand(
            room.RoomId,
            hostId,
            [
                new InitializeCharacterCommand(hostId, "Host Character", Values()),
                new InitializeCharacterCommand(memberId, "Member Character", Values())
            ]))).IsSuccess);

        var projection = await coordinator.GetProjectionAsync(room.RoomId, memberId);
        Assert.True(projection.IsSuccess);
        var snapshot = Assert.IsType<GameSnapshot>(projection.Value);
        Assert.Equal(room.RoomId, snapshot.RoomId);
        Assert.Equal(1, snapshot.Revision);
        Assert.Equal(2, snapshot.Characters.Count);
        Assert.IsNotType<MultiplayerGameState>(snapshot);
        var json = JsonSerializer.Serialize(snapshot);
        Assert.DoesNotContain("PlayerSessionToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Credential", json, StringComparison.OrdinalIgnoreCase);

        var hostCharacter = snapshot.Characters.Single(character => character.OwnerPlayerId == hostId);
        var denied = await coordinator.GetCharacterForOwnerAsync(room.RoomId, hostCharacter.CharacterId, memberId);
        Assert.Equal(GameErrorCode.CharacterNotOwned, denied.Error?.Code);
        var allowed = await coordinator.GetCharacterForOwnerAsync(room.RoomId, hostCharacter.CharacterId, hostId);
        Assert.True(allowed.IsSuccess);
    }

    [Fact]
    public async Task Remove_IsIdempotentAndDisconnectDoesNotRemoveGameState()
    {
        var roomStore = new InMemoryRoomStore();
        var hostId = Guid.NewGuid();
        var room = CreateRoom(roomStore, hostId, "Host");
        var stateStore = new InMemoryGameStateStore();
        var coordinator = new GameCoordinator(roomStore, stateStore);
        Assert.True((await coordinator.InitializeAsync(new InitializeGameCommand(room.RoomId, hostId, [new InitializeCharacterCommand(hostId, "Host", Values())]))).IsSuccess);

        var roomCoordinator = new RoomCoordinator(roomStore);
        Assert.True((await roomCoordinator.SetConnectedAsync(new SetConnectedRoomCommand(room.RoomId, hostId, false))).IsSuccess);
        Assert.True(stateStore.Exists(room.RoomId));
        Assert.True(await coordinator.RemoveAsync(room.RoomId));
        Assert.False(await coordinator.RemoveAsync(room.RoomId));
        Assert.False(stateStore.Exists(room.RoomId));
    }

    private static RoomSession CreateRoom(InMemoryRoomStore store, Guid hostId, string nickname)
    {
        var result = new RoomCoordinator(store).CreateAsync(new CreateRoomCommand(hostId, nickname, 4)).GetAwaiter().GetResult();
        return Assert.IsType<RoomSession>(result.Value);
    }

    private static MultiplayerGameState CreateState(Guid roomId, Guid ownerId, string name) => new(
        roomId,
        1,
        MultiplayerGameStatus.Active,
        DateTimeOffset.UtcNow,
        [new CharacterState(Guid.NewGuid(), ownerId, name, Values())]);

    private static Dictionary<string, int> Values() => new() { ["spotHidden"] = 60 };
}
