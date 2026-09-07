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
            first.Characters,
            combat: CreateCombatSession());

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
        Assert.Same(replacement.Combat, loaded!.Combat);
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
        Assert.Null(state.Combat);
        Assert.All(state.Characters, character => Assert.NotEqual(Guid.Empty, character.CharacterId));
        Assert.Equal("Archivist", state.Characters.Single(character => character.OwnerPlayerId == memberId).Name);
    }

    [Fact]
    public void CharacterState_UsesCanonicalCheckValuesWithoutDuplicateCombatStats()
    {
        var propertyNames = typeof(CharacterState).GetProperties().Select(property => property.Name);

        Assert.DoesNotContain("Dex", propertyNames);
        Assert.DoesNotContain("Fighting", propertyNames);
        Assert.DoesNotContain("Dodge", propertyNames);
    }

    [Fact]
    public async Task InternalCombat_StartUsesExplicitOrderedInvestigatorsAndCanonicalStats()
    {
        var fixture = await CreateCombatGameAsync();
        var start = await StartCombatAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            1,
            [fixture.MemberCharacterId, fixture.HostCharacterId],
            [Opponent("Cultist", 90)]);

        Assert.True(start.IsSuccess);
        var state = Assert.IsType<MultiplayerGameState>(start.State);
        Assert.Equal(2, state.Revision);
        var session = Assert.IsType<CombatSession>(state.Combat);
        Assert.True(session.Active);
        Assert.Equal(1, session.Round);
        Assert.Equal(0, session.TurnIndex);
        Assert.Equal(3, session.Participants.Count);
        Assert.Equal(new[] { "opponent:0", $"character:{fixture.HostCharacterId}", $"character:{fixture.MemberCharacterId}" }, session.Order.Select(id => id.Value));
        Assert.Equal(80, session.Participants.Single(participant => participant.CharacterId == fixture.HostCharacterId).Dex);
        Assert.Equal(65, session.Participants.Single(participant => participant.CharacterId == fixture.MemberCharacterId).Fighting);
        Assert.Empty(session.ActionCounts);
        Assert.Empty(session.ResponseCounts);
        Assert.Empty(session.PendingDamageDispositions);
        Assert.Null(session.PendingExchange);
        Assert.Equal(0, fixture.DiceRoller.PercentileCalls);
    }

    [Fact]
    public async Task InternalCombat_StartAndBeginEnforceAuthorizationRevisionAndPendingInvariants()
    {
        var fixture = await CreateCombatGameAsync();
        var duplicate = await StartCombatAsync(
            fixture.Coordinator, fixture.Room.RoomId, fixture.HostId, 1,
            [fixture.HostCharacterId, fixture.HostCharacterId], [Opponent("Cultist", 90)]);
        Assert.Equal(GameErrorCode.InvalidParticipant, duplicate.ErrorCode);
        Assert.Equal(1, (await fixture.Coordinator.GetProjectionAsync(fixture.Room.RoomId, fixture.HostId)).Value!.Revision);

        var start = await StartCombatAsync(
            fixture.Coordinator, fixture.Room.RoomId, fixture.HostId, 1,
            [fixture.HostCharacterId], [Opponent("Cultist", 90)]);
        Assert.True(start.IsSuccess);
        var session = Assert.IsType<MultiplayerGameState>(start.State).Combat!;
        var attackerId = session.Order[0].Value;
        var defenderId = session.Participants.Single(participant => participant.CharacterId == fixture.HostCharacterId).ParticipantId.Value;

        var wrongActor = await BeginOpposedExchangeAsync(
            fixture.Coordinator, fixture.Room.RoomId, fixture.HostId, 2, defenderId, attackerId);
        Assert.Equal(GameErrorCode.InvalidParticipant, wrongActor.ErrorCode);
        var begin = await BeginOpposedExchangeAsync(
            fixture.Coordinator, fixture.Room.RoomId, fixture.HostId, 2, attackerId, defenderId);
        Assert.True(begin.IsSuccess);
        var afterBegin = Assert.IsType<MultiplayerGameState>(begin.State);
        Assert.Equal(3, afterBegin.Revision);
        var pending = Assert.IsType<PendingCombatExchange>(afterBegin.Combat!.PendingExchange);
        Assert.False(string.IsNullOrWhiteSpace(pending.ExchangeId));
        Assert.Equal(2, pending.CreatedRevision);
        Assert.Equal(0, pending.ResponseCountBefore);
        Assert.NotEmpty(pending.AvailableResponses);
        Assert.Equal(fixture.HostId, pending.DefenderOwnerPlayerId);
        Assert.Empty(afterBegin.Combat.ActionCounts);
        Assert.Empty(afterBegin.Combat.ResponseCounts);
        Assert.Equal(0, afterBegin.Combat.TurnIndex);
        Assert.Equal(0, fixture.DiceRoller.PercentileCalls);

        var second = await BeginOpposedExchangeAsync(
            fixture.Coordinator, fixture.Room.RoomId, fixture.HostId, 3, attackerId, defenderId);
        Assert.Equal(GameErrorCode.PendingConflict, second.ErrorCode);
        Assert.Equal(3, (await fixture.Coordinator.GetProjectionAsync(fixture.Room.RoomId, fixture.HostId)).Value!.Revision);
    }

    [Fact]
    public async Task InternalCombat_StartPreservesInputOrderForEqualDexParticipants()
    {
        var fixture = await CreateCombatGameAsync(
            CombatValues(80, 55, 45),
            CombatValues(80, 65, 50));

        var start = await StartCombatAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            1,
            [fixture.MemberCharacterId, fixture.HostCharacterId],
            [Opponent("Cultist", 60)]);

        Assert.True(start.IsSuccess);
        Assert.Equal(
            new[] { $"character:{fixture.MemberCharacterId}", $"character:{fixture.HostCharacterId}", "opponent:0" },
            start.State!.Combat!.Order.Select(participant => participant.Value));
    }

    [Fact]
    public async Task InternalCombat_StartRejectsDeadMissingCanonicalAndUnrelatedInvestigators()
    {
        var deadFixture = await CreateCombatGameAsync();
        ReplaceCharacterHealth(deadFixture, deadFixture.HostCharacterId, DeadHealth());
        var dead = await StartCombatAsync(
            deadFixture.Coordinator, deadFixture.Room.RoomId, deadFixture.HostId, 1,
            [deadFixture.HostCharacterId], [Opponent("Cultist", 90)]);
        Assert.Equal(GameErrorCode.InvalidParticipant, dead.ErrorCode);

        var missingKeyFixture = await CreateCombatGameAsync(
            new Dictionary<string, int> { ["dex"] = 80, ["fighting_brawl"] = 55 });
        var missingKey = await StartCombatAsync(
            missingKeyFixture.Coordinator, missingKeyFixture.Room.RoomId, missingKeyFixture.HostId, 1,
            [missingKeyFixture.HostCharacterId], [Opponent("Cultist", 90)]);
        Assert.Equal(GameErrorCode.InvalidParticipant, missingKey.ErrorCode);

        var unrelatedFixture = await CreateCombatGameAsync();
        ReplaceCharacterOwner(unrelatedFixture, unrelatedFixture.HostCharacterId, Guid.NewGuid());
        var unrelated = await StartCombatAsync(
            unrelatedFixture.Coordinator, unrelatedFixture.Room.RoomId, unrelatedFixture.HostId, 1,
            [unrelatedFixture.HostCharacterId], [Opponent("Cultist", 90)]);
        Assert.Equal(GameErrorCode.InvalidParticipant, unrelated.ErrorCode);
    }

    [Fact]
    public async Task InternalCombat_StartRejectsDuplicateActiveSession()
    {
        var fixture = await CreateCombatGameAsync();
        Assert.True((await StartCombatAsync(
            fixture.Coordinator, fixture.Room.RoomId, fixture.HostId, 1,
            [fixture.HostCharacterId], [Opponent("Cultist", 90)])).IsSuccess);

        var duplicate = await StartCombatAsync(
            fixture.Coordinator, fixture.Room.RoomId, fixture.HostId, 2,
            [fixture.HostCharacterId], [Opponent("Cultist", 90)]);

        Assert.Equal(GameErrorCode.InvalidCombat, duplicate.ErrorCode);
    }

    [Fact]
    public async Task InternalCombat_BeginRejectsInvalidOrInactiveEnemyAndSnapshotsDistinctReadOnlyResponses()
    {
        var fixture = await CreateCombatGameAsync();
        var start = await StartCombatAsync(
            fixture.Coordinator, fixture.Room.RoomId, fixture.HostId, 1,
            [fixture.HostCharacterId],
            [new CombatOpponent("Cultist", 60, 55, 40, [CombatResponse.Dodge, CombatResponse.Dodge, CombatResponse.FightBack], 1, "player_or_ai")]);
        Assert.True(start.IsSuccess);
        var session = start.State!.Combat!;
        var attackerId = session.Order[0].Value;
        var enemy = session.Participants.Single(participant => participant.ParticipantId.Value == "opponent:0");

        var invalid = await BeginOpposedExchangeAsync(
            fixture.Coordinator, fixture.Room.RoomId, fixture.HostId, 2, attackerId, "missing");
        Assert.Equal(GameErrorCode.InvalidParticipant, invalid.ErrorCode);

        ReplaceCombatParticipant(fixture, enemy.ParticipantId.Value, enemy with { Active = false });
        var inactive = await BeginOpposedExchangeAsync(
            fixture.Coordinator, fixture.Room.RoomId, fixture.HostId, 2, attackerId, enemy.ParticipantId.Value);
        Assert.Equal(GameErrorCode.InvalidParticipant, inactive.ErrorCode);

        ReplaceCombatParticipant(fixture, enemy.ParticipantId.Value, enemy);
        var begin = await BeginOpposedExchangeAsync(
            fixture.Coordinator, fixture.Room.RoomId, fixture.HostId, 2, attackerId, enemy.ParticipantId.Value);
        var responses = Assert.IsAssignableFrom<IList<CombatResponse>>(begin.State!.Combat!.PendingExchange!.AvailableResponses);
        Assert.Equal([CombatResponse.Dodge, CombatResponse.FightBack], responses);
        Assert.Throws<NotSupportedException>(() => responses.Add(CombatResponse.FightBack));
    }

    [Fact]
    public void InternalCombatInterface_IsConsumedByGameCoordinatorAndContainsOnlyAuthorizedSurface()
    {
        var assembly = typeof(GameCoordinator).Assembly;
        var internalCombat = assembly.GetType("Trpg.Multiplayer.Api.Gameplay.IInternalCombatCoordinator")!;

        Assert.True(internalCombat.IsAssignableFrom(typeof(GameCoordinator)));
        Assert.Equal(
            new[] { "BeginOpposedExchangeAsync", "StartCombatAsync" },
            internalCombat.GetMethods().Select(method => method.Name).OrderBy(name => name));
    }

    [Fact]
    public async Task InternalCombat_ResolveRequiresExactExchangeRevisionAuthorityAndCanonicalResponseBeforeDice()
    {
        var fixture = await CreateResolvableCombatGameAsync([1, 100]);
        var pending = await StartAndBeginAgainstOpponentAsync(fixture);
        var revision = pending.State!.Revision;
        var exchangeId = pending.State.Combat!.PendingExchange!.ExchangeId;

        var wrongExchange = await ResolvePendingExchangeAsync(
            fixture.Coordinator, fixture.Room.RoomId, null, revision, "wrong", CombatResponse.Dodge);
        var stale = await ResolvePendingExchangeAsync(
            fixture.Coordinator, fixture.Room.RoomId, null, revision - 1, exchangeId, CombatResponse.Dodge);
        var invalidResponse = await ResolvePendingExchangeAsync(
            fixture.Coordinator, fixture.Room.RoomId, null, revision, exchangeId, (CombatResponse)99);

        Assert.Equal(GameErrorCode.InvalidExchange, wrongExchange.ErrorCode);
        Assert.Equal(GameErrorCode.StateConflict, stale.ErrorCode);
        Assert.Equal(GameErrorCode.InvalidResponse, invalidResponse.ErrorCode);
        Assert.Equal(0, fixture.DiceRoller.PercentileCalls);
        Assert.Equal(revision, (await fixture.Coordinator.GetProjectionAsync(fixture.Room.RoomId, fixture.HostId)).Value!.Revision);

        var resolved = await ResolvePendingExchangeAsync(
            fixture.Coordinator, fixture.Room.RoomId, null, revision, exchangeId, CombatResponse.Dodge);

        Assert.True(resolved.IsSuccess);
        var session = resolved.State!.Combat!;
        Assert.Null(session.PendingExchange);
        Assert.Equal(2, fixture.DiceRoller.PercentileCalls);
        Assert.Equal(1, session.ActionCounts["character:" + fixture.HostCharacterId]);
        Assert.Equal(1, session.ResponseCounts["opponent:0"]);
        Assert.Equal(exchangeId, session.LastExchange!.ExchangeId);
        Assert.Single(session.History);
        Assert.Equal(1, session.TurnIndex);
        Assert.True(session.PendingDamageDispositions.ContainsKey(exchangeId));
        Assert.Equal(exchangeId, session.PendingDamageDispositions[exchangeId].ExchangeId);

        var duplicate = await ResolvePendingExchangeAsync(
            fixture.Coordinator, fixture.Room.RoomId, null, resolved.State.Revision, exchangeId, CombatResponse.Dodge);
        Assert.Equal(GameErrorCode.InvalidExchange, duplicate.ErrorCode);
        Assert.Equal(2, fixture.DiceRoller.PercentileCalls);
        Assert.Single((await GetCombatStateAsync(fixture)).PendingDamageDispositions);
    }

    [Fact]
    public async Task InternalCombat_ResolveRequiresDefenderOwnerForPlayerAndTrustedNullAuthorityForOpponent()
    {
        var playerFixture = await CreateResolvableCombatGameAsync([1, 100]);
        var playerPending = await StartAndBeginAgainstPlayerAsync(playerFixture);
        var playerExchange = playerPending.State!.Combat!.PendingExchange!;

        var attackerOverride = await ResolvePendingExchangeAsync(
            playerFixture.Coordinator, playerFixture.Room.RoomId, playerFixture.MemberId, playerPending.State.Revision,
            playerExchange.ExchangeId, CombatResponse.Dodge);
        Assert.Equal(GameErrorCode.InvalidParticipant, attackerOverride.ErrorCode);
        Assert.Equal(0, playerFixture.DiceRoller.PercentileCalls);

        var playerResolved = await ResolvePendingExchangeAsync(
            playerFixture.Coordinator, playerFixture.Room.RoomId, playerFixture.HostId, playerPending.State.Revision,
            playerExchange.ExchangeId, CombatResponse.Dodge);
        Assert.True(playerResolved.IsSuccess);

        var opponentFixture = await CreateResolvableCombatGameAsync([1, 100]);
        var opponentPending = await StartAndBeginAgainstOpponentAsync(opponentFixture);
        var opponentExchange = opponentPending.State!.Combat!.PendingExchange!;
        var untrustedNpc = await ResolvePendingExchangeAsync(
            opponentFixture.Coordinator, opponentFixture.Room.RoomId, opponentFixture.HostId, opponentPending.State!.Revision,
            opponentExchange.ExchangeId, CombatResponse.Dodge);
        Assert.Equal(GameErrorCode.InvalidParticipant, untrustedNpc.ErrorCode);
        Assert.Equal(0, opponentFixture.DiceRoller.PercentileCalls);

        var trustedNpc = await ResolvePendingExchangeAsync(
            opponentFixture.Coordinator, opponentFixture.Room.RoomId, null, opponentPending.State.Revision,
            opponentExchange.ExchangeId, CombatResponse.Dodge);
        Assert.True(trustedNpc.IsSuccess);
    }

    [Fact]
    public async Task InternalCombat_PassAndEndMaintainPendingAndInactiveInvariantsWithoutDice()
    {
        var fixture = await CreateResolvableCombatGameAsync([1, 100]);
        var pending = await StartAndBeginAgainstOpponentAsync(fixture);
        var blockedPass = await PassCombatTurnAsync(fixture.Coordinator, fixture.Room.RoomId, fixture.HostId, pending.State!.Revision);
        Assert.Equal(GameErrorCode.PendingConflict, blockedPass.ErrorCode);

        var ended = await EndCombatAsync(fixture.Coordinator, fixture.Room.RoomId, fixture.HostId, pending.State.Revision, "ignored");
        Assert.True(ended.IsSuccess);
        var endedSession = ended.State!.Combat!;
        Assert.False(endedSession.Active);
        Assert.Null(endedSession.PendingExchange);
        Assert.Equal("combat_ended_before_resolution", endedSession.EndReason);
        Assert.Null(endedSession.LastExchange);
        Assert.Empty(endedSession.History);
        Assert.Empty(endedSession.PendingDamageDispositions);
        Assert.Equal(0, fixture.DiceRoller.PercentileCalls);

        var passFixture = await CreateResolvableCombatGameAsync([]);
        var started = await StartCombatAsync(passFixture.Coordinator, passFixture.Room.RoomId, passFixture.HostId, 1,
            [passFixture.HostCharacterId], [Opponent("Cultist", 70)]);
        var passed = await PassCombatTurnAsync(passFixture.Coordinator, passFixture.Room.RoomId, passFixture.HostId, started.State!.Revision);
        Assert.True(passed.IsSuccess);
        Assert.Equal(1, passed.State!.Combat!.ActionCounts["character:" + passFixture.HostCharacterId]);
        Assert.Equal(1, passed.State.Combat.TurnIndex);
        Assert.Equal(0, passFixture.DiceRoller.PercentileCalls);
    }

    [Fact]
    public async Task InternalCombat_ResolvedDispositionRegistrySurvivesLastExchangeReplacementAndHistoryTrim()
    {
        var rolls = Enumerable.Range(0, 121).SelectMany(_ => new[] { 1, 100 }).ToArray();
        var fixture = await CreateResolvableCombatGameAsync(rolls);
        var started = await StartCombatAsync(fixture.Coordinator, fixture.Room.RoomId, fixture.HostId, 1,
            [fixture.HostCharacterId], [Opponent("Cultist", 70)]);
        var state = started.State!;
        string? firstExchangeId = null;

        for (var index = 0; index < 121; index++)
        {
            var session = state.Combat!;
            var attackerId = session.Order[session.TurnIndex].Value;
            var defenderId = session.Participants.Single(participant => participant.ParticipantId.Value != attackerId).ParticipantId.Value;
            var requester = attackerId.StartsWith("character:", StringComparison.Ordinal) ? fixture.HostId : fixture.HostId;
            var begin = await BeginOpposedExchangeAsync(fixture.Coordinator, fixture.Room.RoomId, requester, state.Revision, attackerId, defenderId);
            var exchange = begin.State!.Combat!.PendingExchange!;
            firstExchangeId ??= exchange.ExchangeId;
            var resolveRequester = exchange.DefenderOwnerPlayerId;
            var resolved = await ResolvePendingExchangeAsync(fixture.Coordinator, fixture.Room.RoomId, resolveRequester, begin.State.Revision, exchange.ExchangeId, CombatResponse.Dodge);
            Assert.True(resolved.IsSuccess);
            state = resolved.State!;
        }

        var finalSession = state.Combat!;
        Assert.Equal(120, finalSession.History.Count);
        Assert.NotEqual(firstExchangeId, finalSession.History[0].ExchangeId);
        Assert.Equal(finalSession.LastExchange!.ExchangeId, finalSession.History[^1].ExchangeId);
        Assert.True(finalSession.PendingDamageDispositions.ContainsKey(firstExchangeId!));
        Assert.Equal(121, finalSession.PendingDamageDispositions.Count);
        Assert.Equal(242, fixture.DiceRoller.PercentileCalls);
    }

    [Fact]
    public async Task InternalCombat_RoundWrapDelaysDyingCheckUntilFollowingRoundAndRetainsSuccessfulSchedule()
    {
        var fixture = await CreateResolvableCombatGameAsync([1, 1], hostHealth: new CharacterHealthSetup(6, 12, 60));
        Assert.True((await fixture.Coordinator.ApplyDamageAsync(new ApplyDamageCommand(
            fixture.Room.RoomId, fixture.HostCharacterId, "combat-dying", 6, 1))).IsSuccess);

        var started = await StartCombatAsync(
            fixture.Coordinator, fixture.Room.RoomId, fixture.HostId, 2,
            [fixture.HostCharacterId], [Opponent("Cultist", 70)]);
        Assert.True(started.IsSuccess);
        Assert.Equal(new DyingScheduleState(1, null), started.State!.Combat!.DyingSchedule[fixture.HostCharacterId]);

        var firstWrap = await PassCompletedRoundAsync(fixture, started.State);
        Assert.Equal(2, firstWrap.Combat!.Round);
        Assert.Equal(0, fixture.DiceRoller.PercentileCalls);
        Assert.Equal(new DyingScheduleState(1, null), firstWrap.Combat.DyingSchedule[fixture.HostCharacterId]);

        var secondWrap = await PassCompletedRoundAsync(fixture, firstWrap);
        Assert.Equal(3, secondWrap.Combat!.Round);
        Assert.Equal(1, fixture.DiceRoller.PercentileCalls);
        Assert.Equal(new DyingScheduleState(1, 2), secondWrap.Combat.DyingSchedule[fixture.HostCharacterId]);
        Assert.Equal(
            $"combat-round-2-{fixture.HostCharacterId:N}",
            Assert.Single(secondWrap.Characters.Single(character => character.CharacterId == fixture.HostCharacterId).Health.DyingEpisode!.Checks).SourceId);

        var thirdWrap = await PassCompletedRoundAsync(fixture, secondWrap);
        Assert.Equal(2, fixture.DiceRoller.PercentileCalls);
        Assert.Equal(new DyingScheduleState(1, 3), thirdWrap.Combat!.DyingSchedule[fixture.HostCharacterId]);
        Assert.Equal(2, thirdWrap.Characters.Single(character => character.CharacterId == fixture.HostCharacterId).Health.DyingEpisode!.Checks.Count);
    }

    [Fact]
    public async Task InternalCombat_RoundWrapProcessesEligibleDyingInvestigatorsInOrderWithOneRevisionAndKeepsCombatActiveAfterDeath()
    {
        var fixture = await CreateResolvableCombatGameAsync(
            [1, 100],
            hostHealth: new CharacterHealthSetup(6, 12, 60),
            memberHealth: new CharacterHealthSetup(6, 12, 60));
        Assert.True((await fixture.Coordinator.ApplyDamageAsync(new ApplyDamageCommand(
            fixture.Room.RoomId, fixture.HostCharacterId, "host-dying", 6, 1))).IsSuccess);
        Assert.True((await fixture.Coordinator.ApplyDamageAsync(new ApplyDamageCommand(
            fixture.Room.RoomId, fixture.MemberCharacterId, "member-dying", 6, 2))).IsSuccess);

        var started = await StartCombatAsync(
            fixture.Coordinator, fixture.Room.RoomId, fixture.HostId, 3,
            [fixture.HostCharacterId, fixture.MemberCharacterId], [Opponent("Cultist", 60)]);
        Assert.True(started.IsSuccess);
        var firstWrap = await PassCompletedRoundAsync(fixture, started.State!);
        var revisionBeforeChecks = firstWrap.Revision;

        var checkedState = await PassCompletedRoundAsync(fixture, firstWrap);
        var session = checkedState.Combat!;
        Assert.Equal(revisionBeforeChecks + 3, checkedState.Revision);
        Assert.Equal(2, fixture.DiceRoller.PercentileCalls);
        Assert.Equal(new DyingScheduleState(1, 2), session.DyingSchedule[fixture.HostCharacterId]);
        Assert.DoesNotContain(fixture.MemberCharacterId, session.DyingSchedule.Keys);
        Assert.True(checkedState.Characters.Single(character => character.CharacterId == fixture.HostCharacterId).Health.Dying);
        Assert.True(checkedState.Characters.Single(character => character.CharacterId == fixture.MemberCharacterId).Health.Dead);
        Assert.True(session.Active);
        Assert.True(session.Participants.Single(participant => participant.CharacterId == fixture.HostCharacterId).Active);
        Assert.False(session.Participants.Single(participant => participant.CharacterId == fixture.MemberCharacterId).Active);
    }

    [Fact]
    public async Task InternalCombat_RoundWrapRemovesStabilizedScheduleAndObservesFreshDyingEpisode()
    {
        var fixture = await CreateResolvableCombatGameAsync([1, 1], hostHealth: new CharacterHealthSetup(6, 12, 60));
        Assert.True((await fixture.Coordinator.ApplyDamageAsync(new ApplyDamageCommand(
            fixture.Room.RoomId, fixture.HostCharacterId, "initial-dying", 6, 1))).IsSuccess);
        var started = await StartCombatAsync(
            fixture.Coordinator, fixture.Room.RoomId, fixture.HostId, 2,
            [fixture.HostCharacterId], [Opponent("Cultist", 70)]);
        var firstWrap = await PassCompletedRoundAsync(fixture, started.State!);

        Assert.True((await fixture.Coordinator.ResolveFirstAidAsync(new ResolveFirstAidCommand(
            fixture.Room.RoomId, fixture.HostCharacterId, 60, true, "combat-stabilize"))).IsSuccess);
        Assert.True(fixture.StateStore.TryGet(fixture.Room.RoomId, out var stabilized));
        var removed = await PassCompletedRoundAsync(fixture, stabilized!);
        Assert.DoesNotContain(fixture.HostCharacterId, removed.Combat!.DyingSchedule.Keys);

        Assert.True((await fixture.Coordinator.ApplyDamageAsync(new ApplyDamageCommand(
            fixture.Room.RoomId, fixture.HostCharacterId, "fresh-dying", 1, 1))).IsSuccess);
        Assert.True(fixture.StateStore.TryGet(fixture.Room.RoomId, out var freshDying));
        var observed = await PassCompletedRoundAsync(fixture, freshDying!);
        Assert.Equal(new DyingScheduleState(3, null), observed.Combat!.DyingSchedule[fixture.HostCharacterId]);
        Assert.Equal(1, fixture.DiceRoller.PercentileCalls);
    }

    [Fact]
    public async Task InternalCombat_FreshDyingAfterSameRoundStabilizationResetsExistingScheduleWithoutWrapCheck()
    {
        var fixture = await CreateResolvableCombatGameAsync([1, 1, 1], hostHealth: new CharacterHealthSetup(6, 12, 60));
        Assert.True((await fixture.Coordinator.ApplyDamageAsync(new ApplyDamageCommand(
            fixture.Room.RoomId, fixture.HostCharacterId, "initial-dying", 6, 1))).IsSuccess);
        var started = await StartCombatAsync(
            fixture.Coordinator, fixture.Room.RoomId, fixture.HostId, 2,
            [fixture.HostCharacterId], [Opponent("Cultist", 70)]);
        var afterFirstWrap = await PassCompletedRoundAsync(fixture, started.State!);
        var afterPriorCheck = await PassCompletedRoundAsync(fixture, afterFirstWrap);
        Assert.Equal(new DyingScheduleState(1, 2), afterPriorCheck.Combat!.DyingSchedule[fixture.HostCharacterId]);

        Assert.True((await fixture.Coordinator.ResolveFirstAidAsync(new ResolveFirstAidCommand(
            fixture.Room.RoomId, fixture.HostCharacterId, 60, true, "same-round-stabilize"))).IsSuccess);
        Assert.True((await fixture.Coordinator.ApplyDamageAsync(new ApplyDamageCommand(
            fixture.Room.RoomId, fixture.HostCharacterId, "fresh-dying", 1, 1))).IsSuccess);
        Assert.True(fixture.StateStore.TryGet(fixture.Room.RoomId, out var freshDying));

        var wrapped = await PassCompletedRoundAsync(fixture, freshDying!);

        Assert.Equal(new DyingScheduleState(3, null), wrapped.Combat!.DyingSchedule[fixture.HostCharacterId]);
        Assert.Equal(2, fixture.DiceRoller.PercentileCalls);
        Assert.Empty(wrapped.Characters.Single(character => character.CharacterId == fixture.HostCharacterId).Health.DyingEpisode!.Checks);
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
    public async Task Projection_ExposesViewerSafeCombatSummaryWithoutInternalCombatData()
    {
        var fixture = await CreateResolvableCombatGameAsync([1, 100]);
        var started = await StartCombatAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            1,
            [fixture.HostCharacterId, fixture.MemberCharacterId],
            [Opponent("Cultist", 70)]);
        Assert.True(started.IsSuccess);
        var pending = await BeginOpposedExchangeAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            started.State!.Revision,
            "character:" + fixture.HostCharacterId,
            "opponent:0");
        Assert.True(pending.IsSuccess);

        var pendingState = Assert.IsType<MultiplayerGameState>(pending.State);
        var pendingSnapshot = GameProjection.Build(pendingState, fixture.HostId);
        var pendingCombat = Assert.IsType<CombatSnapshot>(pendingSnapshot.Combat);
        Assert.True(pendingCombat.Active);
        Assert.Equal(1, pendingCombat.Round);
        Assert.Equal("character:" + fixture.HostCharacterId, pendingCombat.CurrentActorParticipantId);
        Assert.Equal(pendingState.Combat!.Order.Select(participant => participant.Value), pendingCombat.Participants.Select(participant => participant.ParticipantId));
        Assert.Equal("attacker", pendingCombat.Pending!.Role);
        Assert.Equal("awaiting_response", pendingCombat.Pending.Status);

        var owner = pendingCombat.Participants.Single(participant => participant.CharacterId == fixture.HostCharacterId);
        var otherPlayer = pendingCombat.Participants.Single(participant => participant.CharacterId == fixture.MemberCharacterId);
        var opponent = pendingCombat.Participants.Single(participant => participant.CharacterId is null);
        Assert.True(owner.ViewerOwned);
        Assert.Equal(new CombatParticipantStatsSnapshot(80, 55, 45), owner.Stats);
        Assert.False(otherPlayer.ViewerOwned);
        Assert.Null(otherPlayer.Stats);
        Assert.False(opponent.ViewerOwned);
        Assert.Null(opponent.Stats);

        var resolved = await ResolvePendingExchangeAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            null,
            pendingState.Revision,
            pendingState.Combat.PendingExchange!.ExchangeId,
            CombatResponse.Dodge);
        Assert.True(resolved.IsSuccess);

        var resolvedCombat = Assert.IsType<CombatSnapshot>(GameProjection.Build(resolved.State!, fixture.HostId).Combat);
        Assert.Null(resolvedCombat.Pending);
        Assert.Equal("attacker_hits", resolvedCombat.LastExchange!.Outcome);
        Assert.Equal("character:" + fixture.HostCharacterId, resolvedCombat.LastExchange.WinnerParticipantId);
        Assert.True(resolvedCombat.LastExchange.DispositionPending);

        var combatJson = JsonSerializer.Serialize(resolvedCombat);
        Assert.DoesNotContain("CombatSession", combatJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PendingDamageDispositions", combatJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ResponseAllowance", combatJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ResponsePolicy", combatJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AttackerCheck", combatJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DefenderCheck", combatJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Target", combatJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Roll", combatJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OutnumberedBonusDice", combatJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("History", combatJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DyingSchedule", combatJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SourceId", combatJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Projection_HidesOtherParticipantStatsAndOmitsCombatForNonparticipantsWithoutChangingRevision()
    {
        var fixture = await CreateCombatGameAsync();
        var started = await StartCombatAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            1,
            [fixture.HostCharacterId],
            [Opponent("Cultist", 70)]);
        Assert.True(started.IsSuccess);

        var startedState = Assert.IsType<MultiplayerGameState>(started.State);
        var ownerProjection = GameProjection.Build(startedState, fixture.HostId);
        var nonparticipantProjection = GameProjection.Build(startedState, fixture.MemberId);

        Assert.Equal(startedState.Revision, ownerProjection.Revision);
        Assert.Equal(startedState.Revision, nonparticipantProjection.Revision);
        Assert.NotNull(ownerProjection.Combat);
        Assert.Null(nonparticipantProjection.Combat);
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

    private static async Task<CombatGameFixture> CreateCombatGameAsync(
        IReadOnlyDictionary<string, int>? hostCheckValues = null,
        IReadOnlyDictionary<string, int>? memberCheckValues = null,
        CharacterHealthSetup? hostHealth = null)
    {
        var roomStore = new InMemoryRoomStore();
        var stateStore = new InMemoryGameStateStore();
        var hostId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var room = CreateRoom(roomStore, hostId, "Host");
        Assert.True((await new RoomCoordinator(roomStore).JoinAsync(new JoinRoomCommand(room.RoomId, memberId, "Member"))).IsSuccess);
        var diceRoller = new ThrowingDiceRoller();
        var coordinator = new GameCoordinator(
            roomStore,
            stateStore,
            diceRoller,
            new CocCheckResolutionEngine(),
            new CocHpDamageEngine());
        var initialized = await coordinator.InitializeAsync(new InitializeGameCommand(
            room.RoomId,
            hostId,
            [
                new InitializeCharacterCommand(hostId, "Host", hostCheckValues ?? CombatValues(80, 55, 45), hostHealth ?? Health()),
                new InitializeCharacterCommand(memberId, "Member", memberCheckValues ?? CombatValues(70, 65, 50), Health())
            ]));

        Assert.True(initialized.IsSuccess);
        return new CombatGameFixture(
            coordinator,
            room,
            hostId,
            memberId,
            initialized.Value!.Characters.Single(character => character.OwnerPlayerId == hostId).CharacterId,
            initialized.Value.Characters.Single(character => character.OwnerPlayerId == memberId).CharacterId,
            diceRoller,
            stateStore);
    }

    private static async Task<CombatGameFixture> CreateResolvableCombatGameAsync(
        IReadOnlyList<int> rolls,
        CharacterHealthSetup? hostHealth = null,
        CharacterHealthSetup? memberHealth = null)
    {
        var roomStore = new InMemoryRoomStore();
        var stateStore = new InMemoryGameStateStore();
        var hostId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var room = CreateRoom(roomStore, hostId, "Host");
        Assert.True((await new RoomCoordinator(roomStore).JoinAsync(new JoinRoomCommand(room.RoomId, memberId, "Member"))).IsSuccess);
        var diceRoller = new SequenceDiceRoller(rolls);
        var coordinator = new GameCoordinator(roomStore, stateStore, diceRoller, new CocCheckResolutionEngine(), new CocHpDamageEngine());
        var initialized = await coordinator.InitializeAsync(new InitializeGameCommand(
            room.RoomId,
            hostId,
            [
                new InitializeCharacterCommand(hostId, "Host", CombatValues(80, 55, 45), hostHealth ?? Health()),
                new InitializeCharacterCommand(memberId, "Member", CombatValues(70, 65, 50), memberHealth ?? Health())
            ]));

        Assert.True(initialized.IsSuccess);
        return new CombatGameFixture(
            coordinator, room, hostId, memberId,
            initialized.Value!.Characters.Single(character => character.OwnerPlayerId == hostId).CharacterId,
            initialized.Value.Characters.Single(character => character.OwnerPlayerId == memberId).CharacterId,
            diceRoller, stateStore);
    }

    private sealed record HealthGameFixture(
        GameCoordinator Coordinator,
        RoomSession Room,
        Guid HostId,
        Guid CharacterId,
        FixedDiceRoller DiceRoller,
        InMemoryGameStateStore StateStore);

    private sealed record CombatGameFixture(
        GameCoordinator Coordinator,
        RoomSession Room,
        Guid HostId,
        Guid MemberId,
        Guid HostCharacterId,
        Guid MemberCharacterId,
        CountingDiceRoller DiceRoller,
        InMemoryGameStateStore StateStore);

    private static void ReplaceCharacterOwner(CombatGameFixture fixture, Guid characterId, Guid ownerPlayerId)
    {
        Assert.True(fixture.StateStore.TryGet(fixture.Room.RoomId, out var state));
        var replacement = new MultiplayerGameState(
            state!.RoomId,
            state.Revision,
            state.Status,
            state.CreatedAt,
            state.Characters.Select(character => character.CharacterId == characterId
                ? new CharacterState(character.CharacterId, ownerPlayerId, character.Name, character.CheckValues, character.Health)
                : character),
            state.LastCheck,
            state.Combat);
        Assert.True(fixture.StateStore.TryReplace(state, replacement));
    }

    private static void ReplaceCharacterHealth(
        CombatGameFixture fixture,
        Guid characterId,
        CharacterHealthSetup health)
    {
        Assert.True(fixture.StateStore.TryGet(fixture.Room.RoomId, out var state));
        var replacement = new MultiplayerGameState(
            state!.RoomId,
            state.Revision,
            state.Status,
            state.CreatedAt,
            state.Characters.Select(character => character.CharacterId == characterId
                ? character.WithHealth(new CharacterHealthState(
                    health.CurrentHp,
                    health.MaxHp,
                    health.Con,
                    MajorWound: false,
                    Unconscious: true,
                    Dying: false,
                    Dead: true,
                    History: [],
                    LastDamageEvent: null))
                : character),
            state.LastCheck,
            state.Combat);
        Assert.True(fixture.StateStore.TryReplace(state, replacement));
    }

    private static void ReplaceCombatParticipant(
        CombatGameFixture fixture,
        string participantId,
        CombatParticipantState replacementParticipant)
    {
        Assert.True(fixture.StateStore.TryGet(fixture.Room.RoomId, out var state));
        var replacement = new MultiplayerGameState(
            state!.RoomId,
            state.Revision,
            state.Status,
            state.CreatedAt,
            state.Characters,
            state.LastCheck,
            state.Combat! with
            {
                Participants = state.Combat.Participants
                    .Select(participant => participant.ParticipantId.Value == participantId ? replacementParticipant : participant)
                    .ToArray()
            });
        Assert.True(fixture.StateStore.TryReplace(state, replacement));
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
        [new CharacterState(Guid.NewGuid(), ownerId, name, Values(), new CharacterHealthState(12, 12, 60, false, false, false, false, [], null))]);

    private static CombatSession CreateCombatSession() => new(
        Guid.NewGuid(),
        true,
        1,
        0,
        [],
        [],
        new Dictionary<string, int>(),
        new Dictionary<string, int>(),
        null,
        null,
        [],
        new Dictionary<string, DamageDisposition>(),
        new Dictionary<Guid, DyingScheduleState>(),
        DateTimeOffset.UtcNow,
        null,
        null);

    private static Dictionary<string, int> Values() => new() { ["spotHidden"] = 60 };

    private static Dictionary<string, int> CombatValues(int dex, int fighting, int dodge) => new()
    {
        ["dex"] = dex,
        ["fighting_brawl"] = fighting,
        ["dodge"] = dodge
    };

    private static CombatOpponent Opponent(string label, int dex) => new(
        label, dex, 55, 40, [CombatResponse.Dodge, CombatResponse.FightBack], 1, "player_or_ai");

    private static async Task<InternalCombatResult> StartCombatAsync(
        GameCoordinator coordinator,
        Guid roomId,
        Guid authorizedPlayerId,
        long expectedRevision,
        IReadOnlyList<Guid> characterIds,
        IReadOnlyList<CombatOpponent> opponents) =>
        await InvokeInternalCombatAsync(
            coordinator,
            "StartCombatAsync",
            roomId,
            authorizedPlayerId,
            expectedRevision,
            characterIds,
            CreateOpponentDefinitions(coordinator, opponents));

    private static async Task<InternalCombatResult> BeginOpposedExchangeAsync(
        GameCoordinator coordinator,
        Guid roomId,
        Guid requestingPlayerId,
        long expectedRevision,
        string attackerParticipantId,
        string defenderParticipantId) =>
        await InvokeInternalCombatAsync(
            coordinator,
            "BeginOpposedExchangeAsync",
            roomId,
            requestingPlayerId,
            expectedRevision,
            attackerParticipantId,
            defenderParticipantId);

    private static async Task<InternalCombatResult> ResolvePendingExchangeAsync(
        GameCoordinator coordinator, Guid roomId, Guid? requestingPlayerId, long expectedRevision, string exchangeId, CombatResponse response) =>
        await InvokeInternalCombatAsync(coordinator, "ResolvePendingExchangeAsync", roomId, requestingPlayerId, expectedRevision, exchangeId, response);

    private static async Task<InternalCombatResult> PassCombatTurnAsync(
        GameCoordinator coordinator, Guid roomId, Guid requestingPlayerId, long expectedRevision) =>
        await InvokeInternalCombatAsync(coordinator, "PassCombatTurnAsync", roomId, requestingPlayerId, expectedRevision);

    private static async Task<InternalCombatResult> EndCombatAsync(
        GameCoordinator coordinator, Guid roomId, Guid authorizedPlayerId, long expectedRevision, string reason) =>
        await InvokeInternalCombatAsync(coordinator, "EndCombatAsync", roomId, authorizedPlayerId, expectedRevision, reason);

    private static async Task<InternalCombatResult> StartAndBeginAgainstOpponentAsync(CombatGameFixture fixture)
    {
        var started = await StartCombatAsync(fixture.Coordinator, fixture.Room.RoomId, fixture.HostId, 1,
            [fixture.HostCharacterId], [Opponent("Cultist", 70)]);
        Assert.True(started.IsSuccess);
        return await BeginOpposedExchangeAsync(fixture.Coordinator, fixture.Room.RoomId, fixture.HostId, started.State!.Revision,
            "character:" + fixture.HostCharacterId, "opponent:0");
    }

    private static async Task<InternalCombatResult> StartAndBeginAgainstPlayerAsync(CombatGameFixture fixture)
    {
        var started = await StartCombatAsync(fixture.Coordinator, fixture.Room.RoomId, fixture.HostId, 1,
            [fixture.HostCharacterId, fixture.MemberCharacterId], [Opponent("Cultist", 90)]);
        Assert.True(started.IsSuccess);
        return await BeginOpposedExchangeAsync(fixture.Coordinator, fixture.Room.RoomId, fixture.HostId, started.State!.Revision,
            "opponent:0", "character:" + fixture.HostCharacterId);
    }

    private static async Task<CombatSession> GetCombatStateAsync(CombatGameFixture fixture)
    {
        Assert.True(fixture.StateStore.TryGet(fixture.Room.RoomId, out var state));
        return await Task.FromResult(Assert.IsType<CombatSession>(state!.Combat));
    }

    private static async Task<MultiplayerGameState> PassCompletedRoundAsync(
        CombatGameFixture fixture,
        MultiplayerGameState state)
    {
        var current = state;
        var turnsRemaining = current.Combat!.Order.Count - current.Combat.TurnIndex;
        for (var index = 0; index < turnsRemaining; index++)
        {
            var session = current.Combat!;
            var actor = session.Participants.Single(participant => participant.ParticipantId == session.Order[session.TurnIndex]);
            var requester = actor.OwnerPlayerId ?? fixture.HostId;
            var passed = await PassCombatTurnAsync(
                fixture.Coordinator,
                fixture.Room.RoomId,
                requester,
                current.Revision);
            Assert.True(passed.IsSuccess);
            current = passed.State!;
        }

        return current;
    }

    private static async Task<InternalCombatResult> InvokeInternalCombatAsync(
        GameCoordinator coordinator,
        string methodName,
        params object?[] commandArguments)
    {
        var method = typeof(GameCoordinator).GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var commandType = method.GetParameters().Single().ParameterType;
        var command = Activator.CreateInstance(commandType, commandArguments)!;
        var task = Assert.IsAssignableFrom<Task>(method.Invoke(coordinator, [command]));
        await task;
        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var isSuccess = Assert.IsType<bool>(result.GetType().GetProperty("IsSuccess")!.GetValue(result));
        var error = result.GetType().GetProperty("Error")!.GetValue(result) as GameError;
        var value = result.GetType().GetProperty("Value")!.GetValue(result);
        var state = value?.GetType().GetProperty("State")!.GetValue(value) as MultiplayerGameState;
        return new InternalCombatResult(isSuccess, error?.Code, state);
    }

    private static Array CreateOpponentDefinitions(GameCoordinator coordinator, IReadOnlyList<CombatOpponent> opponents)
    {
        var opponentType = typeof(GameCoordinator).Assembly.GetType("Trpg.Multiplayer.Api.Gameplay.OpponentDefinition")!;
        var definitions = Array.CreateInstance(opponentType, opponents.Count);
        for (var index = 0; index < opponents.Count; index++)
        {
            var opponent = opponents[index];
            definitions.SetValue(Activator.CreateInstance(opponentType,
                opponent.Label,
                opponent.Dex,
                opponent.Fighting,
                opponent.Dodge,
                opponent.AvailableResponses,
                opponent.ResponseAllowance,
                opponent.ResponsePolicy), index);
        }

        return definitions;
    }

    private static CharacterHealthSetup Health() => new(12, 12, 60);

    private static CharacterHealthSetup DeadHealth() => new(0, 12, 60);

    private sealed class FixedDiceRoller(int selectedRoll) : IDiceRoller
    {
        public int PercentileCalls { get; private set; }

        public PercentileDiceRoll RollPercentile(int bonusDice, int penaltyDice)
        {
            PercentileCalls++;
            return new PercentileDiceRoll(selectedRoll, [selectedRoll]);
        }

        public GenericDiceRoll RollDice(DiceRollRequest request)
        {
            throw new InvalidOperationException("No deterministic generic roll remains.");
        }
    }

    private abstract class CountingDiceRoller : IDiceRoller
    {
        public int PercentileCalls { get; private set; }

        public PercentileDiceRoll RollPercentile(int bonusDice, int penaltyDice)
        {
            PercentileCalls++;
            return Roll(bonusDice, penaltyDice);
        }

        public GenericDiceRoll RollDice(DiceRollRequest request)
        {
            throw new InvalidOperationException("No deterministic generic roll remains.");
        }

        protected abstract PercentileDiceRoll Roll(int bonusDice, int penaltyDice);
    }

    private sealed class ThrowingDiceRoller : CountingDiceRoller
    {
        protected override PercentileDiceRoll Roll(int bonusDice, int penaltyDice)
        {
            throw new InvalidOperationException("Combat Start and Begin must not roll dice.");
        }
    }

    private sealed class SequenceDiceRoller(IEnumerable<int> selectedRolls) : CountingDiceRoller
    {
        private readonly Queue<int> selectedRolls = new(selectedRolls);

        protected override PercentileDiceRoll Roll(int bonusDice, int penaltyDice)
        {
            if (!selectedRolls.TryDequeue(out var roll))
            {
                throw new InvalidOperationException("No deterministic combat roll remains.");
            }

            return new PercentileDiceRoll(roll, [roll]);
        }
    }

    private sealed record CombatOpponent(
        string Label,
        int Dex,
        int Fighting,
        int Dodge,
        IReadOnlyList<CombatResponse> AvailableResponses,
        int ResponseAllowance,
        string ResponsePolicy);

    private sealed record InternalCombatResult(bool IsSuccess, GameErrorCode? ErrorCode, MultiplayerGameState? State);
}
