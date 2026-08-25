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
                new InitializeCharacterCommand(hostId, "Investigator", new Dictionary<string, int> { ["spotHidden"] = 60 }, Health()),
                new InitializeCharacterCommand(memberId, "Archivist", new Dictionary<string, int> { ["spotHidden"] = 40 }, Health())
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
            [new InitializeCharacterCommand(unknownId, "Unknown", Values(), Health())]));
        Assert.Equal(GameErrorCode.UnknownPlayer, unknown.Error?.Code);

        var duplicate = await coordinator.InitializeAsync(new InitializeGameCommand(
            room.RoomId,
            hostId,
            [
                new InitializeCharacterCommand(memberId, "One", Values(), Health()),
                new InitializeCharacterCommand(memberId, "Two", Values(), Health())
            ]));
        Assert.Equal(GameErrorCode.DuplicateCharacterOwnership, duplicate.Error?.Code);

        var notHost = await coordinator.InitializeAsync(new InitializeGameCommand(
            room.RoomId,
            memberId,
            [new InitializeCharacterCommand(memberId, "Member", Values(), Health())]));
        Assert.Equal(GameErrorCode.NotHost, notHost.Error?.Code);

        var duplicateKeys = await coordinator.InitializeAsync(new InitializeGameCommand(
            room.RoomId,
            hostId,
            [new InitializeCharacterCommand(hostId, "Duplicate Keys", new Dictionary<string, int>
            {
                ["spotHidden"] = 60,
                ["SpotHidden"] = 40
            }, Health())]));
        Assert.Equal(GameErrorCode.InvalidRoster, duplicateKeys.Error?.Code);
    }

    [Fact]
    public async Task Initialize_RequiresValidCanonicalHealthSetup()
    {
        var roomStore = new InMemoryRoomStore();
        var hostId = Guid.NewGuid();
        var room = CreateRoom(roomStore, hostId, "Host");
        var coordinator = new GameCoordinator(roomStore, new InMemoryGameStateStore());

        var missing = await coordinator.InitializeAsync(new InitializeGameCommand(
            room.RoomId,
            hostId,
            [new InitializeCharacterCommand(hostId, "Missing", Values(), null)]));
        Assert.Equal(GameErrorCode.InvalidHealthSetup, missing.Error?.Code);

        var outOfBounds = await coordinator.InitializeAsync(new InitializeGameCommand(
            room.RoomId,
            hostId,
            [new InitializeCharacterCommand(hostId, "Out of bounds", Values(), new CharacterHealthSetup(13, 12, 60))]));
        Assert.Equal(GameErrorCode.InvalidHealthSetup, outOfBounds.Error?.Code);
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
            coordinator.InitializeAsync(new InitializeGameCommand(firstRoom.RoomId, firstHost, [new InitializeCharacterCommand(firstHost, "One", Values(), Health())])),
            coordinator.InitializeAsync(new InitializeGameCommand(secondRoom.RoomId, secondHost, [new InitializeCharacterCommand(secondHost, "Two", Values(), Health())])));

        Assert.All(results, result => Assert.True(result.IsSuccess));
        var duplicate = await coordinator.InitializeAsync(new InitializeGameCommand(
            firstRoom.RoomId,
            firstHost,
            [new InitializeCharacterCommand(firstHost, "Overwrite", Values(), Health())]));
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
                new InitializeCharacterCommand(hostId, "Host Character", Values(), Health()),
                new InitializeCharacterCommand(memberId, "Member Character", Values(), Health())
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
        var memberCharacter = snapshot.Characters.Single(character => character.OwnerPlayerId == memberId);
        Assert.Null(hostCharacter.Health);
        Assert.NotNull(memberCharacter.Health);
        Assert.Equal(12, memberCharacter.Health.CurrentHp);
        var denied = await coordinator.GetCharacterForOwnerAsync(room.RoomId, hostCharacter.CharacterId, memberId);
        Assert.Equal(GameErrorCode.CharacterNotOwned, denied.Error?.Code);
        var allowed = await coordinator.GetCharacterForOwnerAsync(room.RoomId, hostCharacter.CharacterId, hostId);
        Assert.True(allowed.IsSuccess);
    }

    [Fact]
    public void Projection_ExposesOnlyOwnerStabilizedBooleanAndNoInternalHealthProvenance()
    {
        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var ownerCharacterId = Guid.NewGuid();
        var otherCharacterId = Guid.NewGuid();
        var state = new MultiplayerGameState(
            Guid.NewGuid(),
            3,
            MultiplayerGameStatus.Active,
            DateTimeOffset.UtcNow,
            [
                new CharacterState(
                    ownerCharacterId,
                    ownerId,
                    "Owner",
                    Values(),
                    new CharacterHealthState(
                        1,
                        12,
                        60,
                        true,
                        false,
                        null,
                        new StabilizedConditionState("aid-source", "successful_first_aid", 100, 1, DateTimeOffset.UtcNow),
                        null,
                        [],
                        [],
                        null)),
                new CharacterState(
                    otherCharacterId,
                    otherId,
                    "Other",
                    Values(),
                    new CharacterHealthState(12, 12, 60, false, false, null, null, null, [], [], null))
            ]);

        var projection = GameProjection.Build(state, ownerId);
        var owner = projection.Characters.Single(character => character.CharacterId == ownerCharacterId);
        var other = projection.Characters.Single(character => character.CharacterId == otherCharacterId);

        Assert.True(owner.Health!.Stabilized);
        Assert.True(owner.Health.MajorWound);
        Assert.Null(other.Health);
        var json = JsonSerializer.Serialize(projection);
        Assert.DoesNotContain("DyingCheck", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Treatment", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SourceId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Reason", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("aid-source", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Remove_IsIdempotentAndDisconnectDoesNotRemoveGameState()
    {
        var roomStore = new InMemoryRoomStore();
        var hostId = Guid.NewGuid();
        var room = CreateRoom(roomStore, hostId, "Host");
        var stateStore = new InMemoryGameStateStore();
        var coordinator = new GameCoordinator(roomStore, stateStore);
        Assert.True((await coordinator.InitializeAsync(new InitializeGameCommand(room.RoomId, hostId, [new InitializeCharacterCommand(hostId, "Host", Values(), Health())]))).IsSuccess);

        var roomCoordinator = new RoomCoordinator(roomStore);
        Assert.True((await roomCoordinator.SetConnectedAsync(new SetConnectedRoomCommand(room.RoomId, hostId, false))).IsSuccess);
        Assert.True(stateStore.Exists(room.RoomId));
        Assert.True(await coordinator.RemoveAsync(room.RoomId));
        Assert.False(await coordinator.RemoveAsync(room.RoomId));
        Assert.False(stateStore.Exists(room.RoomId));
    }

    [Fact]
    public async Task InternalDamage_UpdatesOnlyCanonicalHealthAndRevision()
    {
        var roomStore = new InMemoryRoomStore();
        var hostId = Guid.NewGuid();
        var room = CreateRoom(roomStore, hostId, "Host");
        var coordinator = new GameCoordinator(roomStore, new InMemoryGameStateStore());
        var initialized = await coordinator.InitializeAsync(new InitializeGameCommand(
            room.RoomId,
            hostId,
            [new InitializeCharacterCommand(hostId, "Host", Values(), Health())]));
        var characterId = initialized.Value!.Characters.Single().CharacterId;

        var damage = await coordinator.ApplyDamageAsync(new ApplyDamageCommand(room.RoomId, characterId, "internal-1", 5));
        Assert.True(damage.IsSuccess);
        Assert.True(damage.Changed);
        Assert.Equal(2, damage.Value!.Snapshot.Revision);
        Assert.Equal(7, Assert.Single(damage.Value.Snapshot.Characters).Health!.CurrentHp);

        var invalid = await coordinator.ApplyDamageAsync(new ApplyDamageCommand(room.RoomId, characterId, "invalid", 0));
        Assert.Equal(GameErrorCode.InvalidDamage, invalid.Error?.Code);
        var afterInvalid = await coordinator.GetProjectionAsync(room.RoomId, hostId);
        Assert.Equal(2, afterInvalid.Value!.Revision);
    }

    [Fact]
    public async Task InternalDamage_UsesInjectedDiceWhenConRollIsMissing_AndHonorsForcedRoll()
    {
        var roomStore = new InMemoryRoomStore();
        var stateStore = new InMemoryGameStateStore();
        var hostId = Guid.NewGuid();
        var room = CreateRoom(roomStore, hostId, "Host");
        var diceRoller = new FixedDiceRoller(61);
        var coordinator = new GameCoordinator(
            roomStore,
            stateStore,
            diceRoller,
            new CocCheckResolutionEngine(),
            new CocHpDamageEngine());
        var initialized = await coordinator.InitializeAsync(new InitializeGameCommand(
            room.RoomId,
            hostId,
            [new InitializeCharacterCommand(hostId, "Host", Values(), Health())]));
        var characterId = initialized.Value!.Characters.Single().CharacterId;

        var injected = await coordinator.ApplyDamageAsync(new ApplyDamageCommand(
            room.RoomId,
            characterId,
            "major-injected",
            6));

        Assert.True(injected.IsSuccess);
        Assert.Equal(1, diceRoller.PercentileCalls);
        Assert.Equal(61, injected.Value!.Event!.ConCheck!.Roll);
        Assert.False(injected.Value.Event.ConCheck.Success);

        var forced = await coordinator.ApplyDamageAsync(new ApplyDamageCommand(
            room.RoomId,
            characterId,
            "major-forced",
            6,
            1));

        Assert.True(forced.IsSuccess);
        Assert.Equal(1, diceRoller.PercentileCalls);
        Assert.Equal(1, forced.Value!.Event!.ConCheck!.Roll);
        Assert.True(forced.Value.Event.ConCheck.Success);
    }

    [Fact]
    public async Task InternalDamage_IsSerializedAndDoesNotCrossRoomBoundaries()
    {
        var roomStore = new InMemoryRoomStore();
        var firstHost = Guid.NewGuid();
        var secondHost = Guid.NewGuid();
        var firstRoom = CreateRoom(roomStore, firstHost, "First");
        var secondRoom = CreateRoom(roomStore, secondHost, "Second");
        var coordinator = new GameCoordinator(roomStore, new InMemoryGameStateStore());
        var first = await coordinator.InitializeAsync(new InitializeGameCommand(firstRoom.RoomId, firstHost, [new InitializeCharacterCommand(firstHost, "First", Values(), Health())]));
        var second = await coordinator.InitializeAsync(new InitializeGameCommand(secondRoom.RoomId, secondHost, [new InitializeCharacterCommand(secondHost, "Second", Values(), Health())]));
        var firstCharacter = first.Value!.Characters.Single().CharacterId;
        var secondCharacter = second.Value!.Characters.Single().CharacterId;

        var mutations = await Task.WhenAll(
            coordinator.ApplyDamageAsync(new ApplyDamageCommand(firstRoom.RoomId, firstCharacter, "first-a", 1)),
            coordinator.ApplyDamageAsync(new ApplyDamageCommand(firstRoom.RoomId, firstCharacter, "first-b", 1)));
        Assert.All(mutations, result => Assert.True(result.IsSuccess));
        var firstProjection = await coordinator.GetProjectionAsync(firstRoom.RoomId, firstHost);
        var secondProjection = await coordinator.GetProjectionAsync(secondRoom.RoomId, secondHost);
        Assert.Equal(3, firstProjection.Value!.Revision);
        Assert.Equal(10, Assert.Single(firstProjection.Value.Characters).Health!.CurrentHp);
        Assert.Equal(1, secondProjection.Value!.Revision);
        Assert.Equal(12, Assert.Single(secondProjection.Value.Characters).Health!.CurrentHp);
        Assert.NotEqual(firstCharacter, secondCharacter);
    }

    [Fact]
    public async Task Stabilization_DyingRoundsUseInjectedRollsCommitRevisionsAndAdvanceOrdinals()
    {
        var fixture = await CreateHealthGameAsync(currentHp: 6, diceRoll: 1);
        var damage = await fixture.Coordinator.ApplyDamageAsync(new ApplyDamageCommand(
            fixture.Room.RoomId,
            fixture.CharacterId,
            "dying-source",
            6,
            1));

        Assert.True(damage.IsSuccess);
        Assert.True(damage.Value!.Snapshot.Characters.Single().Health!.Dying);

        var first = await fixture.Coordinator.ResolveDyingRoundAsync(new ResolveDyingRoundCommand(
            fixture.Room.RoomId,
            fixture.CharacterId,
            "round-1"));
        var second = await fixture.Coordinator.ResolveDyingRoundAsync(new ResolveDyingRoundCommand(
            fixture.Room.RoomId,
            fixture.CharacterId,
            "round-2"));

        Assert.True(first.IsSuccess);
        Assert.Equal(1, first.Value!.DyingCheck!.Ordinal);
        Assert.Equal(1, first.Value.DyingCheck.Roll);
        Assert.Equal(3, first.Value.Snapshot.Revision);
        Assert.True(fixture.StateStore.TryGet(fixture.Room.RoomId, out var firstState));
        Assert.True(firstState!.Characters.Single().Health.DyingEpisode!.RoundChecksManaged);
        Assert.True(second.IsSuccess);
        Assert.Equal(2, second.Value!.DyingCheck!.Ordinal);
        Assert.Equal(4, second.Value.Snapshot.Revision);
        Assert.True(fixture.StateStore.TryGet(fixture.Room.RoomId, out var secondState));
        Assert.Equal(2, secondState!.Characters.Single().Health.DyingEpisode!.Checks.Count);
        Assert.Equal(2, fixture.DiceRoller.PercentileCalls);
    }

    [Fact]
    public async Task Stabilization_DyingFailureCreatesDeadStateAndRejectsSubsequentFirstAid()
    {
        var fixture = await CreateHealthGameAsync(currentHp: 6, diceRoll: 61);
        var damage = await fixture.Coordinator.ApplyDamageAsync(new ApplyDamageCommand(
            fixture.Room.RoomId,
            fixture.CharacterId,
            "dying-source",
            6,
            1));

        Assert.True(damage.IsSuccess);
        var failedRound = await fixture.Coordinator.ResolveDyingRoundAsync(new ResolveDyingRoundCommand(
            fixture.Room.RoomId,
            fixture.CharacterId,
            "round-failure"));

        Assert.True(failedRound.IsSuccess);
        Assert.False(failedRound.Value!.DyingCheck!.Success);
        Assert.True(failedRound.Value.Snapshot.Characters.Single().Health!.Dead);
        Assert.False(failedRound.Value.Snapshot.Characters.Single().Health!.Dying);
        Assert.Equal(3, failedRound.Value.Snapshot.Revision);

        var firstAid = await fixture.Coordinator.ResolveFirstAidAsync(new ResolveFirstAidCommand(
            fixture.Room.RoomId,
            fixture.CharacterId,
            60,
            true,
            "dead-aid"));

        Assert.False(firstAid.IsSuccess);
        Assert.Equal(3, (await fixture.Coordinator.GetProjectionAsync(fixture.Room.RoomId, fixture.HostId)).Value!.Revision);
    }

    [Fact]
    public async Task Stabilization_FirstAidRecordsSuccessAndFailureAndFreshDamageClearsStaleStabilization()
    {
        var successFixture = await CreateHealthGameAsync(currentHp: 6, diceRoll: 1);
        Assert.True((await successFixture.Coordinator.ApplyDamageAsync(new ApplyDamageCommand(
            successFixture.Room.RoomId,
            successFixture.CharacterId,
            "dying-source",
            6,
            1))).IsSuccess);

        var stabilized = await successFixture.Coordinator.ResolveFirstAidAsync(new ResolveFirstAidCommand(
            successFixture.Room.RoomId,
            successFixture.CharacterId,
            60,
            true,
            "aid-success"));

        Assert.True(stabilized.IsSuccess);
        Assert.True(stabilized.Value!.Treatment!.Success);
        Assert.True(stabilized.Value.Treatment.StabilizedDying);
        Assert.True(successFixture.StateStore.TryGet(successFixture.Room.RoomId, out var stabilizedState));
        Assert.Equal("successful_first_aid", stabilizedState!.Characters.Single().Health.Stabilized!.Reason);
        Assert.Equal(3, stabilized.Value.Snapshot.Revision);

        var freshDamage = await successFixture.Coordinator.ApplyDamageAsync(new ApplyDamageCommand(
            successFixture.Room.RoomId,
            successFixture.CharacterId,
            "fresh-damage",
            6,
            1));

        Assert.True(freshDamage.IsSuccess);
        Assert.Equal(4, freshDamage.Value!.Snapshot.Revision);
        Assert.True(freshDamage.Value.Snapshot.Characters.Single().Health!.Dying);
        Assert.True(successFixture.StateStore.TryGet(successFixture.Room.RoomId, out var freshDamageState));
        Assert.Null(freshDamageState!.Characters.Single().Health.Stabilized);

        var failureFixture = await CreateHealthGameAsync(currentHp: 6, diceRoll: 61);
        Assert.True((await failureFixture.Coordinator.ApplyDamageAsync(new ApplyDamageCommand(
            failureFixture.Room.RoomId,
            failureFixture.CharacterId,
            "dying-source",
            6,
            1))).IsSuccess);

        var failedAid = await failureFixture.Coordinator.ResolveFirstAidAsync(new ResolveFirstAidCommand(
            failureFixture.Room.RoomId,
            failureFixture.CharacterId,
            60,
            true,
            "aid-failure"));

        Assert.True(failedAid.IsSuccess);
        Assert.False(failedAid.Value!.Treatment!.Success);
        Assert.True(failedAid.Value.Snapshot.Characters.Single().Health!.Dying);
        Assert.True(failureFixture.StateStore.TryGet(failureFixture.Room.RoomId, out var failedAidState));
        Assert.Single(failedAidState!.Characters.Single().Health.TreatmentHistory);
        Assert.Equal(3, failedAid.Value.Snapshot.Revision);
    }

    [Fact]
    public async Task Stabilization_ValidatesPrerequisitesAndSerializesConcurrentRounds()
    {
        var fixture = await CreateHealthGameAsync(currentHp: 6, diceRoll: 1);
        var noDying = await fixture.Coordinator.ResolveDyingRoundAsync(new ResolveDyingRoundCommand(
            fixture.Room.RoomId,
            fixture.CharacterId,
            "missing-dying"));
        var invalidAid = await fixture.Coordinator.ResolveFirstAidAsync(new ResolveFirstAidCommand(
            fixture.Room.RoomId,
            fixture.CharacterId,
            0,
            true,
            "invalid-aid"));
        var outsideHour = await fixture.Coordinator.ResolveFirstAidAsync(new ResolveFirstAidCommand(
            fixture.Room.RoomId,
            fixture.CharacterId,
            60,
            false,
            "late-aid"));

        Assert.False(noDying.IsSuccess);
        Assert.False(invalidAid.IsSuccess);
        Assert.False(outsideHour.IsSuccess);
        Assert.Equal(1, (await fixture.Coordinator.GetProjectionAsync(fixture.Room.RoomId, fixture.HostId)).Value!.Revision);

        Assert.True((await fixture.Coordinator.ApplyDamageAsync(new ApplyDamageCommand(
            fixture.Room.RoomId,
            fixture.CharacterId,
            "dying-source",
            6,
            1))).IsSuccess);
        var rounds = await Task.WhenAll(
            fixture.Coordinator.ResolveDyingRoundAsync(new ResolveDyingRoundCommand(fixture.Room.RoomId, fixture.CharacterId, "concurrent-1")),
            fixture.Coordinator.ResolveDyingRoundAsync(new ResolveDyingRoundCommand(fixture.Room.RoomId, fixture.CharacterId, "concurrent-2")));

        Assert.All(rounds, result => Assert.True(result.IsSuccess));
        Assert.Equal(4, (await fixture.Coordinator.GetProjectionAsync(fixture.Room.RoomId, fixture.HostId)).Value!.Revision);
        Assert.True(fixture.StateStore.TryGet(fixture.Room.RoomId, out var finalState));
        Assert.Equal(2, finalState!.Characters.Single().Health.DyingEpisode!.Checks.Count);
    }

    [Fact]
    public async Task Stabilization_IsolatedAcrossRooms()
    {
        var roomStore = new InMemoryRoomStore();
        var stateStore = new InMemoryGameStateStore();
        var firstHost = Guid.NewGuid();
        var secondHost = Guid.NewGuid();
        var firstRoom = CreateRoom(roomStore, firstHost, "First");
        var secondRoom = CreateRoom(roomStore, secondHost, "Second");
        var coordinator = new GameCoordinator(
            roomStore,
            stateStore,
            new FixedDiceRoller(1),
            new CocCheckResolutionEngine(),
            new CocHpDamageEngine(),
            new CocHealthStabilizationEngine());
        var first = await coordinator.InitializeAsync(new InitializeGameCommand(
            firstRoom.RoomId,
            firstHost,
            [new InitializeCharacterCommand(firstHost, "First", Values(), new CharacterHealthSetup(6, 12, 60))]));
        var second = await coordinator.InitializeAsync(new InitializeGameCommand(
            secondRoom.RoomId,
            secondHost,
            [new InitializeCharacterCommand(secondHost, "Second", Values(), new CharacterHealthSetup(6, 12, 60))]));
        var firstCharacterId = first.Value!.Characters.Single().CharacterId;
        var secondCharacterId = second.Value!.Characters.Single().CharacterId;

        Assert.True((await coordinator.ApplyDamageAsync(new ApplyDamageCommand(firstRoom.RoomId, firstCharacterId, "first-damage", 6, 1))).IsSuccess);
        Assert.True((await coordinator.ApplyDamageAsync(new ApplyDamageCommand(secondRoom.RoomId, secondCharacterId, "second-damage", 6, 1))).IsSuccess);
        var resolved = await coordinator.ResolveDyingRoundAsync(new ResolveDyingRoundCommand(firstRoom.RoomId, firstCharacterId, "first-round"));

        Assert.True(resolved.IsSuccess);
        Assert.Equal(3, (await coordinator.GetProjectionAsync(firstRoom.RoomId, firstHost)).Value!.Revision);
        var secondProjection = await coordinator.GetProjectionAsync(secondRoom.RoomId, secondHost);
        Assert.Equal(2, secondProjection.Value!.Revision);
        Assert.True(secondProjection.Value.Characters.Single().Health!.Dying);
        Assert.True(stateStore.TryGet(secondRoom.RoomId, out var secondState));
        Assert.Empty(secondState!.Characters.Single().Health.DyingEpisode!.Checks);
    }

    private static async Task<HealthGameFixture> CreateHealthGameAsync(int currentHp, int diceRoll)
    {
        var roomStore = new InMemoryRoomStore();
        var stateStore = new InMemoryGameStateStore();
        var hostId = Guid.NewGuid();
        var room = CreateRoom(roomStore, hostId, "Host");
        var diceRoller = new FixedDiceRoller(diceRoll);
        var coordinator = new GameCoordinator(
            roomStore,
            stateStore,
            diceRoller,
            new CocCheckResolutionEngine(),
            new CocHpDamageEngine(),
            new CocHealthStabilizationEngine());
        var initialized = await coordinator.InitializeAsync(new InitializeGameCommand(
            room.RoomId,
            hostId,
            [new InitializeCharacterCommand(hostId, "Host", Values(), new CharacterHealthSetup(currentHp, 12, 60))]));

        return new HealthGameFixture(coordinator, room, hostId, initialized.Value!.Characters.Single().CharacterId, diceRoller, stateStore);
    }

    private sealed record HealthGameFixture(
        GameCoordinator Coordinator,
        RoomSession Room,
        Guid HostId,
        Guid CharacterId,
        FixedDiceRoller DiceRoller,
        InMemoryGameStateStore StateStore);

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
        [new CharacterState(Guid.NewGuid(), ownerId, name, Values(), new CharacterHealthState(12, 12, 60, false, false, false, false, [], null))]);

    private static Dictionary<string, int> Values() => new() { ["spotHidden"] = 60 };

    private static CharacterHealthSetup Health() => new(12, 12, 60);

    private sealed class FixedDiceRoller(int selectedRoll) : IDiceRoller
    {
        public int PercentileCalls { get; private set; }

        public PercentileDiceRoll RollPercentile(int bonusDice, int penaltyDice)
        {
            PercentileCalls++;
            return new PercentileDiceRoll(selectedRoll, [selectedRoll]);
        }
    }
}
