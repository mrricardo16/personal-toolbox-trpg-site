using System.Text.Json;
using Trpg.Multiplayer.Api.Gameplay;
using Trpg.Multiplayer.Api.Realtime;
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
    public void CombatDamageProfile_DefaultCharacterLoadoutIsCanonicalUnarmed()
    {
        var character = new CharacterState(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Investigator",
            CombatValues(80, 55, 45),
            new CharacterHealthState(12, 12, 60, false, false, false, false, [], null));

        Assert.Equal(0, character.CombatLoadout.FixedArmor);
        Assert.Equal("unarmed", character.CombatLoadout.Weapon.WeaponId);
        Assert.Equal("徒手/拳脚", character.CombatLoadout.Weapon.Label);
        Assert.Equal("1d3", character.CombatLoadout.Weapon.Damage.Text);
        Assert.True(character.CombatLoadout.Weapon.AddsDamageBonus);
        Assert.Equal("melee_non_impaling", character.CombatLoadout.Weapon.Mode);
    }

    [Fact]
    public async Task InternalCombat_StartSnapshotsCanonicalInvestigatorAndOpponentDamageProfiles()
    {
        var fixture = await CreateCombatGameAsync();
        var start = await StartCombatAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            1,
            [fixture.HostCharacterId],
            [Opponent("Cultist", 90) with
            {
                Str = 90,
                Siz = 80,
                CurrentHp = 11,
                MaxHp = 14,
                FixedArmor = 2,
                Weapon = Weapon("ritual-club", "仪式棍", "1d6")
            }]);

        Assert.True(start.IsSuccess);
        var session = Assert.IsType<CombatSession>(start.State!.Combat);
        var investigator = session.Participants.Single(participant => participant.CharacterId == fixture.HostCharacterId);
        Assert.Equal(80, investigator.Dex);
        Assert.Equal(55, investigator.Fighting);
        Assert.Equal(45, investigator.Dodge);
        Assert.Equal(60, investigator.DamageProfile.Str);
        Assert.Equal(50, investigator.DamageProfile.Siz);
        Assert.Equal(CocCombatDamageRules.DeriveDamageBonus(60, 50), investigator.DamageProfile.DamageBonus);
        Assert.Equal("unarmed", investigator.DamageProfile.Weapon.WeaponId);
        Assert.Equal(0, investigator.DamageProfile.FixedArmor);
        Assert.Null(investigator.OpponentVitality);

        var opponent = session.Participants.Single(participant => participant.ParticipantId.Value == "opponent:0");
        Assert.Equal(90, opponent.DamageProfile.Str);
        Assert.Equal(80, opponent.DamageProfile.Siz);
        Assert.Equal(CocCombatDamageRules.DeriveDamageBonus(90, 80), opponent.DamageProfile.DamageBonus);
        Assert.Equal("ritual-club", opponent.DamageProfile.Weapon.WeaponId);
        Assert.Equal("1d6", opponent.DamageProfile.Weapon.Damage.Text);
        Assert.Equal(2, opponent.DamageProfile.FixedArmor);
        Assert.Equal(new OpponentVitalityState(11, 14), opponent.OpponentVitality);
        Assert.Equal(0, fixture.DiceRoller.PercentileCalls);
    }

    [Fact]
    public async Task InternalCombat_StartSnapshotsTypedPrivateNpcPolicy()
    {
        var fixture = await CreateCombatGameAsync();
        var started = await StartCombatAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            1,
            [fixture.HostCharacterId],
            [Opponent("Cultist", 90) with { NpcResponsePolicy = CombatResponse.FightBack }]);

        Assert.True(started.IsSuccess);
        var opponent = Assert.Single(started.State!.Combat!.Participants, participant => participant.Kind == "opponent");
        Assert.Equal(CombatResponse.FightBack, opponent.NpcResponsePolicy);
        Assert.Contains(opponent.NpcResponsePolicy!.Value, opponent.AvailableResponses);
    }

    [Fact]
    public async Task InternalCombat_StartRejectsNpcPolicyOutsideSnapshottedResponses()
    {
        var fixture = await CreateCombatGameAsync();
        var result = await StartCombatAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            1,
            [fixture.HostCharacterId],
            [Opponent("Cultist", 90) with
            {
                NpcResponsePolicy = CombatResponse.FightBack,
                AvailableResponses = [CombatResponse.Dodge]
            }]);

        Assert.False(result.IsSuccess);
        Assert.Equal(GameErrorCode.InvalidCombat, result.ErrorCode);
        Assert.Equal(1, (await fixture.Coordinator.GetProjectionAsync(fixture.Room.RoomId, fixture.HostId)).Value!.Revision);
    }

    [Theory]
    [InlineData("str", null)]
    [InlineData("str", 0)]
    [InlineData("str", 101)]
    [InlineData("siz", null)]
    [InlineData("siz", 0)]
    [InlineData("siz", 101)]
    public async Task InternalCombat_StartRejectsMissingOrInvalidInvestigatorDamageProfileWithoutMutation(
        string key,
        int? value)
    {
        var fixture = await CreateCombatGameAsync();
        var checkValues = CombatValues(80, 55, 45);
        if (value.HasValue)
        {
            checkValues[key] = value.Value;
        }
        else
        {
            checkValues.Remove(key);
        }

        ReplaceCharacterCombatProfile(fixture, fixture.HostCharacterId, checkValues, DefaultLoadout());
        Assert.True(fixture.StateStore.TryGet(fixture.Room.RoomId, out var before));

        var start = await StartCombatAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            before!.Revision,
            [fixture.HostCharacterId],
            [Opponent("Cultist", 90)]);

        Assert.Equal(GameErrorCode.InvalidParticipant, start.ErrorCode);
        Assert.True(fixture.StateStore.TryGet(fixture.Room.RoomId, out var after));
        Assert.Same(before, after);
        Assert.Equal(1, after!.Revision);
        Assert.Null(after.Combat);
        Assert.Equal(0, fixture.DiceRoller.PercentileCalls);
    }

    [Fact]
    public async Task CombatDamageProfile_StartRejectsMissingCharacterLoadoutWithoutMutation()
    {
        var fixture = await CreateCombatGameAsync();
        ReplaceCharacterCombatProfile(fixture, fixture.HostCharacterId, CombatValues(80, 55, 45), null!);
        Assert.True(fixture.StateStore.TryGet(fixture.Room.RoomId, out var before));

        var start = await StartCombatAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            before!.Revision,
            [fixture.HostCharacterId],
            [Opponent("Cultist", 90)]);

        Assert.Equal(GameErrorCode.InvalidParticipant, start.ErrorCode);
        Assert.True(fixture.StateStore.TryGet(fixture.Room.RoomId, out var after));
        Assert.Same(before, after);
        Assert.Null(after!.Combat);
        Assert.Equal(0, fixture.DiceRoller.PercentileCalls);
    }

    [Theory]
    [InlineData("str_zero")]
    [InlineData("str_high")]
    [InlineData("siz_zero")]
    [InlineData("siz_high")]
    [InlineData("current_hp_zero")]
    [InlineData("max_hp_below_current")]
    [InlineData("armor_negative")]
    [InlineData("armor_high")]
    [InlineData("weapon_missing")]
    [InlineData("weapon_mode")]
    [InlineData("weapon_expression")]
    public async Task InternalCombat_StartRejectsInvalidOpponentDamageProfileWithoutMutation(string invalidCase)
    {
        var fixture = await CreateCombatGameAsync();
        var valid = Opponent("Cultist", 90);
        var opponent = invalidCase switch
        {
            "str_zero" => valid with { Str = 0 },
            "str_high" => valid with { Str = 1000 },
            "siz_zero" => valid with { Siz = 0 },
            "siz_high" => valid with { Siz = 1000 },
            "current_hp_zero" => valid with { CurrentHp = 0 },
            "max_hp_below_current" => valid with { CurrentHp = 11, MaxHp = 10 },
            "armor_negative" => valid with { FixedArmor = -1 },
            "armor_high" => valid with { FixedArmor = 100 },
            "weapon_missing" => valid with { Weapon = null },
            "weapon_mode" => valid with { Weapon = valid.Weapon! with { Mode = "firearm" } },
            "weapon_expression" => valid with
            {
                Weapon = valid.Weapon! with { Damage = new DiceExpression("not-dice", 1, 3, 0) }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null)
        };
        Assert.True(fixture.StateStore.TryGet(fixture.Room.RoomId, out var before));

        var start = await StartCombatAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            before!.Revision,
            [fixture.HostCharacterId],
            [opponent]);

        Assert.Equal(GameErrorCode.InvalidCombat, start.ErrorCode);
        Assert.True(fixture.StateStore.TryGet(fixture.Room.RoomId, out var after));
        Assert.Same(before, after);
        Assert.Equal(1, after!.Revision);
        Assert.Null(after.Combat);
        Assert.Equal(0, fixture.DiceRoller.PercentileCalls);
    }

    [Fact]
    public async Task CombatDamageProfile_StartSnapshotDoesNotDriftWithSourceCharacterProfile()
    {
        var fixture = await CreateCombatGameAsync();
        var initialValues = CombatValues(80, 55, 45, 90, 80);
        var initialLoadout = new CharacterCombatLoadout(Weapon("sabre", "军刀", "1d8"), 3);
        ReplaceCharacterCombatProfile(fixture, fixture.HostCharacterId, initialValues, initialLoadout);

        var start = await StartCombatAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            1,
            [fixture.HostCharacterId],
            [Opponent("Cultist", 90)]);
        Assert.True(start.IsSuccess);

        ReplaceCharacterCombatProfile(
            fixture,
            fixture.HostCharacterId,
            CombatValues(20, 25, 30, 40, 45),
            new CharacterCombatLoadout(Weapon("club", "棍棒", "1d6", addsDamageBonus: false), 1));

        Assert.True(fixture.StateStore.TryGet(fixture.Room.RoomId, out var afterSourceChange));
        var participant = afterSourceChange!.Combat!.Participants.Single(
            candidate => candidate.CharacterId == fixture.HostCharacterId);
        Assert.Equal(80, participant.Dex);
        Assert.Equal(55, participant.Fighting);
        Assert.Equal(45, participant.Dodge);
        Assert.Equal(90, participant.DamageProfile.Str);
        Assert.Equal(80, participant.DamageProfile.Siz);
        Assert.Equal(CocCombatDamageRules.DeriveDamageBonus(90, 80), participant.DamageProfile.DamageBonus);
        Assert.Equal("sabre", participant.DamageProfile.Weapon.WeaponId);
        Assert.Equal("1d8", participant.DamageProfile.Weapon.Damage.Text);
        Assert.Equal(3, participant.DamageProfile.FixedArmor);
        Assert.Equal(0, fixture.DiceRoller.PercentileCalls);
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
        Assert.Empty(session.DamageDispositions);
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
            [Opponent("Cultist", 60) with
            {
                AvailableResponses = [CombatResponse.Dodge, CombatResponse.Dodge, CombatResponse.FightBack]
            }]);
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
    public void InternalCombat_PlayerBeginUsesSeparatedAuthorityAndSharedMutationCore()
    {
        var coordinatorSource = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Trpg.Multiplayer.Api", "Gameplay", "GameCoordinator.cs"));

        Assert.Contains("ValidatePlayerBeginAuthority", coordinatorSource, StringComparison.Ordinal);
        Assert.Contains("LoadBeginOpposedExchangeContext", coordinatorSource, StringComparison.Ordinal);
        Assert.Contains("CommitBeginOpposedExchange", coordinatorSource, StringComparison.Ordinal);
        Assert.DoesNotContain("trusted =", coordinatorSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Guid.Empty", coordinatorSource, StringComparison.Ordinal);
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
        var disposition = Assert.Single(session.DamageDispositions).Value;
        Assert.Equal(exchangeId, disposition.Disposition.ExchangeId);
        Assert.Equal(DamageDispositionStatus.Pending, disposition.Status);
        Assert.Null(disposition.Result);
        Assert.Equal(resolved.State.Revision, disposition.Disposition.CreatedGameRevision);

        var duplicate = await ResolvePendingExchangeAsync(
            fixture.Coordinator, fixture.Room.RoomId, null, resolved.State.Revision, exchangeId, CombatResponse.Dodge);
        Assert.Equal(GameErrorCode.PendingConflict, duplicate.ErrorCode);
        Assert.Equal(2, fixture.DiceRoller.PercentileCalls);
        Assert.Single((await GetCombatStateAsync(fixture)).DamageDispositions);
    }

    [Fact]
    public async Task InternalCombat_ResolveNoHitCreatesNoDamageDisposition()
    {
        var fixture = await CreateResolvableCombatGameAsync([100, 100]);
        var pending = await StartAndBeginAgainstOpponentAsync(fixture);
        var exchangeId = pending.State!.Combat!.PendingExchange!.ExchangeId;

        var resolved = await ResolvePendingExchangeAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            null,
            pending.State.Revision,
            exchangeId,
            CombatResponse.Dodge);

        Assert.True(resolved.IsSuccess);
        Assert.Empty(resolved.State!.Combat!.DamageDispositions);
        Assert.Null(resolved.State.Combat.LastExchange!.DamageDisposition);
        Assert.Equal(2, fixture.DiceRoller.PercentileCalls);
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
        Assert.Empty(endedSession.DamageDispositions);
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
    public async Task InternalCombat_PlayerPassUsesSeparatedAuthorityAndSharedMutationCore()
    {
        var flags = System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.NonPublic;
        Assert.NotNull(typeof(GameCoordinator).GetMethod("LoadPassCombatTurnContext", flags));
        Assert.NotNull(typeof(GameCoordinator).GetMethod("ValidatePlayerPassAuthority", flags));
        Assert.NotNull(typeof(GameCoordinator).GetMethod("CommitPassCombatTurn", flags));

        var fixture = await CreateCombatDamageGameAsync([]);
        var started = await StartCombatAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            1,
            [fixture.HostCharacterId, fixture.MemberCharacterId],
            [Opponent("Cultist", 60)]);
        Assert.True(started.IsSuccess);

        var order = started.State!.Combat!.Order;
        var firstReplacementAttempts = fixture.StateStore.ReplacementAttempts;
        fixture.Notifier.Reset();

        var ownerPass = await PassCombatTurnAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            started.State.Revision);

        Assert.True(ownerPass.IsSuccess);
        Assert.Equal(started.State.Revision + 1, ownerPass.State!.Revision);
        Assert.Equal(order, ownerPass.State.Combat!.Order);
        Assert.Equal(1, ownerPass.State.Combat.ActionCounts["character:" + fixture.HostCharacterId]);
        Assert.Equal(firstReplacementAttempts + 1, fixture.StateStore.ReplacementAttempts);
        Assert.Equal(1, fixture.Notifier.GameSnapshotCalls);

        fixture.Notifier.Reset();
        var memberPass = await PassCombatTurnAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.MemberId,
            ownerPass.State.Revision);
        Assert.True(memberPass.IsSuccess);
        Assert.Equal(ownerPass.State.Revision + 1, memberPass.State!.Revision);
        Assert.Equal(1, fixture.Notifier.GameSnapshotCalls);

        fixture.Notifier.Reset();
        var hostControlledOpponentPass = await PassCombatTurnAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            memberPass.State.Revision);
        Assert.True(hostControlledOpponentPass.IsSuccess);
        Assert.Equal(memberPass.State.Revision + 1, hostControlledOpponentPass.State!.Revision);
        Assert.Equal(2, hostControlledOpponentPass.State.Combat!.Round);
        Assert.Equal(order, hostControlledOpponentPass.State.Combat.Order);
        Assert.Equal(1, fixture.Notifier.GameSnapshotCalls);
    }

    [Fact]
    public async Task CombatDamageGate_ResolvedHitBlocksBeginPassAndManualEndWithoutMutationOrDice()
    {
        var fixture = await CreateResolvableCombatGameAsync([1, 100]);
        var pending = await StartAndBeginAgainstOpponentAsync(fixture);
        var exchangeId = pending.State!.Combat!.PendingExchange!.ExchangeId;
        var resolved = await ResolvePendingExchangeAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            null,
            pending.State.Revision,
            exchangeId,
            CombatResponse.Dodge);
        Assert.True(resolved.IsSuccess);
        Assert.True(fixture.StateStore.TryGet(fixture.Room.RoomId, out var before));
        var session = before!.Combat!;
        var attacker = session.Order[session.TurnIndex].Value;
        var defender = session.Participants.Single(participant => participant.ParticipantId.Value != attacker).ParticipantId.Value;

        var begin = await BeginOpposedExchangeAsync(
            fixture.Coordinator, fixture.Room.RoomId, fixture.HostId, before.Revision - 1, attacker, defender);
        var pass = await PassCombatTurnAsync(
            fixture.Coordinator, fixture.Room.RoomId, fixture.HostId, before.Revision - 1);
        var end = await EndCombatAsync(
            fixture.Coordinator, fixture.Room.RoomId, fixture.HostId, before.Revision - 1, "ignored");

        Assert.Equal(GameErrorCode.PendingConflict, begin.ErrorCode);
        Assert.Equal(GameErrorCode.PendingConflict, pass.ErrorCode);
        Assert.Equal(GameErrorCode.PendingConflict, end.ErrorCode);
        Assert.True(fixture.StateStore.TryGet(fixture.Room.RoomId, out var after));
        Assert.Same(before, after);
        Assert.Equal(before.Revision, after!.Revision);
        Assert.True(after.Combat!.Active);
        Assert.Single(after.Combat.DamageDispositions);
        Assert.Equal(2, fixture.DiceRoller.PercentileCalls);
    }

    [Fact]
    public async Task CombatDamageGate_LegacyPendingDispositionBlocksPendingExchangeResolutionBeforeDice()
    {
        var fixture = await CreateResolvableCombatGameAsync([1, 100]);
        var pending = await StartAndBeginAgainstOpponentAsync(fixture);
        var exchange = pending.State!.Combat!.PendingExchange!;
        ReplaceDamageDispositions(
            fixture,
            new Dictionary<string, DamageDispositionState>
            {
                ["legacy"] = PendingDisposition("legacy", exchange.AttackerParticipantId, exchange.DefenderParticipantId, 1)
            });
        Assert.True(fixture.StateStore.TryGet(fixture.Room.RoomId, out var before));

        var blocked = await ResolvePendingExchangeAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            null,
            before!.Revision,
            exchange.ExchangeId,
            CombatResponse.Dodge);

        Assert.Equal(GameErrorCode.PendingConflict, blocked.ErrorCode);
        Assert.True(fixture.StateStore.TryGet(fixture.Room.RoomId, out var after));
        Assert.Same(before, after);
        Assert.Equal(0, fixture.DiceRoller.PercentileCalls);
    }

    [Fact]
    public void CombatDamageGate_LegacyBlockerUsesRevisionThenOrdinalExchangeId()
    {
        var owner = new CombatParticipantId("owner");
        var target = new CombatParticipantId("target");
        var dispositions = new Dictionary<string, DamageDispositionState>
        {
            ["z-later"] = PendingDisposition("z-later", owner, target, 8),
            ["z-first-tie"] = PendingDisposition("z-first-tie", owner, target, 3),
            ["a-first-tie"] = PendingDisposition("a-first-tie", owner, target, 3),
            ["consumed-earlier"] = ConsumedDisposition("consumed-earlier", owner, target, 1)
        };

        var method = typeof(GameCoordinator).GetMethod(
            "FindBlockingDamageDisposition",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var blocker = Assert.IsType<DamageDispositionState>(method.Invoke(null, [dispositions]));

        Assert.Equal("a-first-tie", blocker.Disposition.ExchangeId);
    }

    [Fact]
    public void ResolveCombatDamage_InternalResolutionInterfaceOwnsTheTrustedCommand()
    {
        var assembly = typeof(GameCoordinator).Assembly;
        var internalCombat = assembly.GetType("Trpg.Multiplayer.Api.Gameplay.IInternalCombatResolutionCoordinator")!;

        Assert.True(internalCombat.IsAssignableFrom(typeof(GameCoordinator)));
        Assert.Contains(internalCombat.GetMethods(), method => method.Name == "ResolveCombatDamageAsync");
    }

    [Fact]
    public async Task Projection_ResolveCombatDamage_ConsumedReplayWithOriginalStaleRevisionReturnsStoredResultWithoutSecondNotification()
    {
        var fixture = await CreateCombatDamageGameAsync([2]);
        var pendingState = await CreatePendingDamageDispositionAsync(fixture);
        var exchangeId = Assert.Single(pendingState.Combat!.DamageDispositions).Key;
        var originalExpectedRevision = pendingState.Revision;
        var pendingProjection = Assert.IsType<CombatSnapshot>(GameProjection.Build(pendingState, fixture.HostId).Combat);

        Assert.True(pendingProjection.LastExchange!.DispositionPending);
        Assert.Null(pendingProjection.LastDamage);

        var first = await ResolveCombatDamageAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            originalExpectedRevision,
            exchangeId);

        Assert.True(first.IsSuccess);
        Assert.True(first.Changed);
        Assert.Equal(originalExpectedRevision + 1, first.State!.Revision);
        var storedResult = Assert.IsType<CombatDamageResult>(first.Damage);
        Assert.Equal(1, fixture.DiceRoller.GenericCalls);
        Assert.Equal(2, fixture.DiceRoller.PercentileCalls);
        Assert.Equal(0, fixture.HpDamageEngine.Calls);
        Assert.Equal(1, fixture.Notifier.GameSnapshotCalls);
        var replacementsAfterFirstConsumption = fixture.StateStore.ReplacementAttempts;
        var stateAfterFirstConsumption = GetRequiredState(fixture.StateStore, fixture.Room.RoomId);
        var sessionAfterFirstConsumption = stateAfterFirstConsumption.Combat!;
        var targetAfterFirstConsumption = sessionAfterFirstConsumption.Participants.Single(
            participant => participant.ParticipantId == storedResult.TargetParticipantId);
        var consumedProjection = Assert.IsType<CombatSnapshot>(GameProjection.Build(stateAfterFirstConsumption, fixture.HostId).Combat);

        Assert.False(consumedProjection.LastExchange!.DispositionPending);
        Assert.Equal(exchangeId, consumedProjection.LastDamage!.ExchangeId);
        Assert.Same(pendingState.Combat.LastExchange, sessionAfterFirstConsumption.LastExchange);
        Assert.Equal(pendingState.Combat.History, sessionAfterFirstConsumption.History);

        var replay = await ResolveCombatDamageAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            originalExpectedRevision,
            exchangeId);

        Assert.True(replay.IsSuccess);
        Assert.False(replay.Changed);
        Assert.Same(stateAfterFirstConsumption, replay.State);
        Assert.Same(storedResult, replay.Damage);
        Assert.Equal(originalExpectedRevision + 1, replay.State!.Revision);
        Assert.Equal(replacementsAfterFirstConsumption, fixture.StateStore.ReplacementAttempts);
        Assert.Equal(1, fixture.DiceRoller.GenericCalls);
        Assert.Equal(2, fixture.DiceRoller.PercentileCalls);
        Assert.Equal(0, fixture.HpDamageEngine.Calls);
        Assert.Equal(1, fixture.Notifier.GameSnapshotCalls);
        Assert.Equal(pendingState.Combat.History, replay.State.Combat!.History);
        Assert.Equal(pendingState.Combat.Round, replay.State.Combat.Round);
        Assert.Equal(pendingState.Combat.TurnIndex, replay.State.Combat.TurnIndex);
        Assert.Equal(pendingState.Combat.Order, replay.State.Combat.Order);
        Assert.Equal(pendingState.Combat.ActionCounts, replay.State.Combat.ActionCounts);
        Assert.Equal(pendingState.Combat.ResponseCounts, replay.State.Combat.ResponseCounts);
        Assert.Equal(targetAfterFirstConsumption, replay.State.Combat.Participants.Single(
            participant => participant.ParticipantId == storedResult.TargetParticipantId));
        var replayProjection = Assert.IsType<CombatSnapshot>(GameProjection.Build(replay.State, fixture.HostId).Combat);
        Assert.False(replayProjection.LastExchange!.DispositionPending);
        Assert.Equal(exchangeId, replayProjection.LastDamage!.ExchangeId);
    }

    [Fact]
    public async Task ResolveCombatDamage_PendingStaleRevisionFailsBeforeThrowingDice()
    {
        var fixture = await CreateCombatDamageGameAsync([]);
        var pendingState = await CreatePendingDamageDispositionAsync(fixture);
        var exchangeId = Assert.Single(pendingState.Combat!.DamageDispositions).Key;

        var result = await ResolveCombatDamageAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            pendingState.Revision - 1,
            exchangeId);

        Assert.Equal(GameErrorCode.StateConflict, result.ErrorCode);
        Assert.False(result.Changed);
        Assert.Equal(0, fixture.DiceRoller.GenericCalls);
        Assert.Equal(2, fixture.DiceRoller.PercentileCalls);
        Assert.Equal(0, fixture.StateStore.ConsumptionReplacementAttempts);
        Assert.Equal(0, fixture.Notifier.GameSnapshotCalls);
    }

    [Fact]
    public async Task ResolveCombatDamage_MissingExchangeFailsBeforeThrowingDice()
    {
        var fixture = await CreateCombatDamageGameAsync([]);
        var pendingState = await CreatePendingDamageDispositionAsync(fixture);

        var result = await ResolveCombatDamageAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            pendingState.Revision,
            "missing-exchange");

        Assert.Equal(GameErrorCode.InvalidExchange, result.ErrorCode);
        Assert.False(result.Changed);
        Assert.Equal(0, fixture.DiceRoller.GenericCalls);
        Assert.Equal(2, fixture.DiceRoller.PercentileCalls);
        Assert.Equal(0, fixture.StateStore.ConsumptionReplacementAttempts);
        Assert.Equal(0, fixture.Notifier.GameSnapshotCalls);
    }

    [Fact]
    public async Task ResolveCombatDamage_NonBlockingPendingExchangeFailsBeforeThrowingDice()
    {
        var fixture = await CreateCombatDamageGameAsync([]);
        var pendingState = await CreatePendingDamageDispositionAsync(fixture);
        var current = Assert.Single(pendingState.Combat!.DamageDispositions).Value;
        var earlier = PendingDisposition(
            "earlier-blocker",
            current.Disposition.OwnerParticipantId,
            current.Disposition.TargetParticipantId,
            current.Disposition.CreatedGameRevision - 1);
        ReplaceDamageDispositions(
            fixture.StateStore,
            fixture.Room.RoomId,
            pendingState.Combat.DamageDispositions.Append(
                new KeyValuePair<string, DamageDispositionState>(earlier.Disposition.ExchangeId, earlier))
                .ToDictionary(pair => pair.Key, pair => pair.Value));
        var replaced = GetRequiredState(fixture.StateStore, fixture.Room.RoomId);

        var result = await ResolveCombatDamageAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            replaced.Revision,
            current.Disposition.ExchangeId);

        Assert.Equal(GameErrorCode.PendingConflict, result.ErrorCode);
        Assert.False(result.Changed);
        Assert.Equal(0, fixture.DiceRoller.GenericCalls);
        Assert.Equal(2, fixture.DiceRoller.PercentileCalls);
        Assert.Equal(0, fixture.StateStore.ConsumptionReplacementAttempts);
        Assert.Equal(0, fixture.Notifier.GameSnapshotCalls);
    }

    [Fact]
    public async Task ResolveCombatDamage_MalformedStatusResultFailsAsInternalInvariantBeforeThrowingDice()
    {
        var fixture = await CreateCombatDamageGameAsync([]);
        var pendingState = await CreatePendingDamageDispositionAsync(fixture);
        var entry = Assert.Single(pendingState.Combat!.DamageDispositions).Value;
        ReplaceDamageDispositions(
            fixture.StateStore,
            fixture.Room.RoomId,
            new Dictionary<string, DamageDispositionState>
            {
                [entry.Disposition.ExchangeId] = entry with
                {
                    Status = DamageDispositionStatus.Consumed,
                    Result = null
                }
            });
        fixture.StateStore.ResetConsumptionReplacementAttempts();

        var exception = await Record.ExceptionAsync(() => ResolveCombatDamageAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            pendingState.Revision,
            entry.Disposition.ExchangeId));

        Assert.NotNull(exception);
        Assert.Equal("CombatDamageStateInvariantException", exception.GetType().Name);
        Assert.Equal(0, fixture.DiceRoller.GenericCalls);
        Assert.Equal(2, fixture.DiceRoller.PercentileCalls);
        Assert.Equal(0, fixture.StateStore.ConsumptionReplacementAttempts);
        Assert.Equal(0, fixture.Notifier.GameSnapshotCalls);
    }

    [Fact]
    public async Task ResolveCombatDamage_MalformedCanonicalProfileFailsAsInternalInvariantBeforeThrowingDice()
    {
        var fixture = await CreateCombatDamageGameAsync([]);
        var pendingState = await CreatePendingDamageDispositionAsync(fixture);
        var entry = Assert.Single(pendingState.Combat!.DamageDispositions).Value;
        var owner = pendingState.Combat.Participants.Single(
            participant => participant.ParticipantId == entry.Disposition.OwnerParticipantId);
        ReplaceCombatParticipant(
            fixture.StateStore,
            fixture.Room.RoomId,
            owner.ParticipantId.Value,
            owner with { DamageProfile = owner.DamageProfile with { Weapon = null! } });

        var exception = await Record.ExceptionAsync(() => ResolveCombatDamageAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            pendingState.Revision,
            entry.Disposition.ExchangeId));

        Assert.NotNull(exception);
        Assert.Equal("CombatDamageStateInvariantException", exception.GetType().Name);
        Assert.Equal(0, fixture.DiceRoller.GenericCalls);
        Assert.Equal(2, fixture.DiceRoller.PercentileCalls);
        Assert.Equal(0, fixture.StateStore.ConsumptionReplacementAttempts);
        Assert.Equal(0, fixture.Notifier.GameSnapshotCalls);
    }

    [Fact]
    public async Task ResolveCombatDamage_TargetAlreadyIneligibleConsumesOnceWithoutDiceHpVitalityOrTurnRepair()
    {
        var fixture = await CreateCombatDamageGameAsync([]);
        var pendingState = await CreatePendingDamageDispositionAsync(fixture);
        var entry = Assert.Single(pendingState.Combat!.DamageDispositions).Value;
        var target = pendingState.Combat.Participants.Single(
            participant => participant.ParticipantId == entry.Disposition.TargetParticipantId);
        ReplaceCombatParticipant(
            fixture.StateStore,
            fixture.Room.RoomId,
            target.ParticipantId.Value,
            target with { Active = false });
        var before = GetRequiredState(fixture.StateStore, fixture.Room.RoomId);
        var replacementsBefore = fixture.StateStore.ReplacementAttempts;

        var result = await ResolveCombatDamageAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            before.Revision,
            entry.Disposition.ExchangeId);

        Assert.True(result.IsSuccess);
        Assert.True(result.Changed);
        Assert.Equal(before.Revision + 1, result.State!.Revision);
        Assert.Equal(CombatDamageOutcome.TargetAlreadyIneligible, result.Damage!.Outcome);
        Assert.Null(result.Damage.WeaponResult);
        Assert.Null(result.Damage.DamageBonusResult);
        Assert.False(result.Damage.HpDamageApplied);
        Assert.Equal(result.Damage.HpBefore, result.Damage.HpAfter);
        Assert.True(result.Damage.TargetDefeated);
        Assert.Equal(replacementsBefore + 1, fixture.StateStore.ReplacementAttempts);
        Assert.Equal(1, fixture.StateStore.ConsumptionReplacementAttempts);
        Assert.Equal(0, fixture.DiceRoller.GenericCalls);
        Assert.Equal(2, fixture.DiceRoller.PercentileCalls);
        Assert.Equal(0, fixture.HpDamageEngine.Calls);
        Assert.Equal(1, fixture.Notifier.GameSnapshotCalls);
        var consumed = result.State.Combat!.DamageDispositions[entry.Disposition.ExchangeId];
        Assert.Equal(DamageDispositionStatus.Consumed, consumed.Status);
        Assert.Same(result.Damage, consumed.Result);
        Assert.Equal(before.Combat!.Round, result.State.Combat.Round);
        Assert.Equal(before.Combat.TurnIndex, result.State.Combat.TurnIndex);
        Assert.Equal(before.Combat.Order, result.State.Combat.Order);
        Assert.Equal(before.Combat.History, result.State.Combat.History);
        Assert.Equal(target.OpponentVitality, result.State.Combat.Participants.Single(
            participant => participant.ParticipantId == target.ParticipantId).OpponentVitality);
        var consumedProjection = Assert.IsType<CombatSnapshot>(GameProjection.Build(result.State, fixture.HostId).Combat);
        Assert.False(consumedProjection.LastExchange!.DispositionPending);
        Assert.Equal(entry.Disposition.ExchangeId, consumedProjection.LastDamage!.ExchangeId);
        Assert.Equal("target_already_ineligible", consumedProjection.LastDamage.Outcome);

        var replay = await ResolveCombatDamageAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            before.Revision,
            entry.Disposition.ExchangeId);
        Assert.True(replay.IsSuccess);
        Assert.False(replay.Changed);
        Assert.Same(result.Damage, replay.Damage);
        Assert.Equal(before.Revision + 1, replay.State!.Revision);
        Assert.Equal(1, fixture.StateStore.ConsumptionReplacementAttempts);
        Assert.Equal(0, fixture.DiceRoller.GenericCalls);
        Assert.Equal(0, fixture.HpDamageEngine.Calls);
        Assert.Equal(1, fixture.Notifier.GameSnapshotCalls);
    }

    [Fact]
    public async Task Projection_ResolveCombatDamage_PostRngStoreFailurePublishesNoSnapshotAndProhibitsReroll()
    {
        var fixture = await CreateCombatDamageGameAsync([2]);
        var pendingState = await CreatePendingDamageDispositionAsync(fixture);
        var exchangeId = Assert.Single(pendingState.Combat!.DamageDispositions).Key;
        fixture.StateStore.ArmConsumptionFailure(() => fixture.DiceRoller.GenericCalls > 0);

        var exception = await Record.ExceptionAsync(() => ResolveCombatDamageAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            pendingState.Revision,
            exchangeId));

        Assert.NotNull(exception);
        Assert.Equal("CombatDamageCommitInvariantException", exception.GetType().Name);
        Assert.Contains("must not be re-rolled", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, fixture.DiceRoller.GenericCalls);
        Assert.Equal(2, fixture.DiceRoller.PercentileCalls);
        Assert.Equal(1, fixture.StateStore.ConsumptionReplacementAttempts);
        Assert.Equal(0, fixture.HpDamageEngine.Calls);
        Assert.Equal(0, fixture.Notifier.GameSnapshotCalls);
        var unchanged = GetRequiredState(fixture.StateStore, fixture.Room.RoomId);
        Assert.Same(pendingState, unchanged);
        Assert.Equal(DamageDispositionStatus.Pending, unchanged.Combat!.DamageDispositions[exchangeId].Status);
        Assert.Null(unchanged.Combat.DamageDispositions[exchangeId].Result);
    }

    [Fact]
    public async Task ResolveCombatDamage_InvestigatorOrdinaryLossUsesStableEventKeyAndReplayHasNoSecondHpEventOrConRoll()
    {
        var fixture = await CreateCombatDamageGameAsync([2]);
        var pendingState = await CreatePendingInvestigatorDamageDispositionAsync(
            fixture,
            Opponent("Cultist", 90));
        var entry = Assert.Single(pendingState.Combat!.DamageDispositions);

        var result = await ResolveCombatDamageAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            pendingState.Revision,
            entry.Key);

        Assert.True(result.IsSuccess);
        Assert.Equal(pendingState.Revision + 1, result.State!.Revision);
        Assert.Equal(12, result.Damage!.HpBefore);
        Assert.Equal(10, result.Damage.HpAfter);
        Assert.True(result.Damage.HpDamageApplied);
        Assert.False(result.Damage.TargetDefeated);
        Assert.Equal(1, fixture.HpDamageEngine.Calls);
        Assert.Equal($"combat:{entry.Key}", fixture.HpDamageEngine.LastInput!.EventKey);
        Assert.Null(fixture.HpDamageEngine.LastInput.ConRoll);
        Assert.Equal(2, fixture.DiceRoller.PercentileCalls);
        var damaged = result.State.Characters.Single(character => character.CharacterId == fixture.HostCharacterId);
        Assert.Equal(10, damaged.Health.CurrentHp);
        Assert.Equal($"combat:{entry.Key}", Assert.Single(damaged.Health.History).EventKey);

        var replay = await ResolveCombatDamageAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            pendingState.Revision,
            entry.Key);

        Assert.True(replay.IsSuccess);
        Assert.False(replay.Changed);
        Assert.Same(result.Damage, replay.Damage);
        Assert.Equal(1, fixture.HpDamageEngine.Calls);
        Assert.Equal(2, fixture.DiceRoller.PercentileCalls);
        Assert.Single(replay.State!.Characters.Single(
            character => character.CharacterId == fixture.HostCharacterId).Health.History);
    }

    [Theory]
    [InlineData(40, false)]
    [InlineData(100, true)]
    public async Task ResolveCombatDamage_InvestigatorMajorWoundUsesOneSecureConRollAndInvalidatesStabilization(
        int conRoll,
        bool expectedUnconscious)
    {
        var fixture = await CreateCombatDamageGameAsync([6], [40, 100, conRoll]);
        ReplaceCharacterHealth(
            fixture.StateStore,
            fixture.Room.RoomId,
            fixture.HostCharacterId,
            new CharacterHealthState(
                12,
                12,
                60,
                majorWound: false,
                unconscious: false,
                dyingEpisode: null,
                stabilized: new StabilizedConditionState("aid", "prior_stabilization", 60, 1, null),
                deadCondition: null,
                treatmentHistory: [],
                history: [],
                lastDamageEvent: null));
        var pendingState = await CreatePendingInvestigatorDamageDispositionAsync(
            fixture,
            Opponent("Cultist", 90) with
            {
                Weapon = Weapon("club", "木棒", "1d6", addsDamageBonus: false)
            });
        var entry = Assert.Single(pendingState.Combat!.DamageDispositions);

        var result = await ResolveCombatDamageAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            pendingState.Revision,
            entry.Key);

        var health = result.State!.Characters.Single(
            character => character.CharacterId == fixture.HostCharacterId).Health;
        Assert.True(result.IsSuccess);
        Assert.Equal(6, health.CurrentHp);
        Assert.True(health.MajorWound);
        Assert.Equal(expectedUnconscious, health.Unconscious);
        Assert.NotNull(health.Stabilized);
        Assert.Equal(conRoll, health.LastDamageEvent!.ConCheck!.Roll);
        Assert.Equal(3, fixture.DiceRoller.PercentileCalls);
        Assert.Equal(conRoll, fixture.HpDamageEngine.LastInput!.ConRoll);
    }

    [Fact]
    public async Task ResolveCombatDamage_InvestigatorDyingRemainsActiveAndIsScheduledWithoutImmediateCheck()
    {
        var fixture = await CreateCombatDamageGameAsync(
            [6],
            [40, 100, 40],
            hostHealth: new CharacterHealthSetup(6, 12, 60));
        ReplaceCharacterHealth(
            fixture.StateStore,
            fixture.Room.RoomId,
            fixture.HostCharacterId,
            new CharacterHealthState(
                6,
                12,
                60,
                majorWound: false,
                unconscious: false,
                dyingEpisode: null,
                stabilized: new StabilizedConditionState("aid", "stale_stabilization", 60, 1, null),
                deadCondition: null,
                treatmentHistory: [],
                history: [],
                lastDamageEvent: null));
        var pendingState = await CreatePendingInvestigatorDamageDispositionAsync(
            fixture,
            Opponent("Cultist", 90) with
            {
                Weapon = Weapon("club", "木棒", "1d6", addsDamageBonus: false)
            });
        var entry = Assert.Single(pendingState.Combat!.DamageDispositions);

        var result = await ResolveCombatDamageAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            pendingState.Revision,
            entry.Key);

        var session = result.State!.Combat!;
        var character = result.State.Characters.Single(
            candidate => candidate.CharacterId == fixture.HostCharacterId);
        var participant = session.Participants.Single(
            candidate => candidate.CharacterId == fixture.HostCharacterId);
        Assert.True(result.IsSuccess);
        Assert.Equal(0, character.Health.CurrentHp);
        Assert.True(character.Health.Dying);
        Assert.False(character.Health.Dead);
        Assert.Null(character.Health.Stabilized);
        Assert.True(participant.Active);
        Assert.True(session.Active);
        Assert.Equal(new DyingScheduleState(session.Round, null), session.DyingSchedule[fixture.HostCharacterId]);
        Assert.Empty(character.Health.DyingEpisode!.Checks);
        Assert.Equal(participant.ParticipantId, session.Order[session.TurnIndex]);
        Assert.False(result.Damage!.TargetDefeated);
    }

    [Fact]
    public async Task ResolveCombatDamage_InvestigatorInstantDeathInactivatesAndEndsOnlyWhenNoInvestigatorRemains()
    {
        var soleFixture = await CreateCombatDamageGameAsync([12], [40, 100]);
        var solePending = await CreatePendingInvestigatorDamageDispositionAsync(
            soleFixture,
            Opponent("Cultist", 90) with
            {
                Weapon = Weapon("great-club", "巨棒", "1d12", addsDamageBonus: false)
            });
        var soleEntry = Assert.Single(solePending.Combat!.DamageDispositions);

        var soleResult = await ResolveCombatDamageAsync(
            soleFixture.Coordinator,
            soleFixture.Room.RoomId,
            solePending.Revision,
            soleEntry.Key);

        var soleSession = soleResult.State!.Combat!;
        Assert.True(soleResult.State.Characters.Single(
            character => character.CharacterId == soleFixture.HostCharacterId).Health.Dead);
        Assert.False(soleSession.Participants.Single(
            participant => participant.CharacterId == soleFixture.HostCharacterId).Active);
        Assert.False(soleSession.Active);
        Assert.Equal("investigators_defeated", soleSession.EndReason);
        Assert.True(soleResult.Damage!.TargetDefeated);
        Assert.Equal(2, soleFixture.DiceRoller.PercentileCalls);

        var partyFixture = await CreateCombatDamageGameAsync([12], [40, 100]);
        var partyPending = await CreatePendingInvestigatorDamageDispositionAsync(
            partyFixture,
            Opponent("Cultist", 90) with
            {
                Weapon = Weapon("great-club", "巨棒", "1d12", addsDamageBonus: false)
            },
            [partyFixture.HostCharacterId, partyFixture.MemberCharacterId]);
        var partyEntry = Assert.Single(partyPending.Combat!.DamageDispositions);

        var partyResult = await ResolveCombatDamageAsync(
            partyFixture.Coordinator,
            partyFixture.Room.RoomId,
            partyPending.Revision,
            partyEntry.Key);

        var partySession = partyResult.State!.Combat!;
        Assert.True(partySession.Active);
        Assert.Null(partySession.EndReason);
        Assert.False(partySession.Participants.Single(
            participant => participant.CharacterId == partyFixture.HostCharacterId).Active);
        Assert.True(partySession.Participants.Single(
            participant => participant.CharacterId == partyFixture.MemberCharacterId).Active);
        Assert.Equal(
            new CombatParticipantId($"character:{partyFixture.MemberCharacterId}"),
            partySession.Order[partySession.TurnIndex]);
    }

    [Fact]
    public async Task ResolveCombatDamage_ZeroNetConsumesOneRevisionWithoutHpOrConRoll()
    {
        var fixture = await CreateCombatDamageGameAsync(
            [2],
            [40, 100],
            hostLoadout: new CharacterCombatLoadout(DefaultLoadout().Weapon, FixedArmor: 2));
        var pendingState = await CreatePendingInvestigatorDamageDispositionAsync(
            fixture,
            Opponent("Cultist", 90));
        var entry = Assert.Single(pendingState.Combat!.DamageDispositions);

        var result = await ResolveCombatDamageAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            pendingState.Revision,
            entry.Key);

        Assert.True(result.IsSuccess);
        Assert.Equal(pendingState.Revision + 1, result.State!.Revision);
        Assert.Equal(0, result.Damage!.NetDamage);
        Assert.Equal(12, result.Damage.HpBefore);
        Assert.Equal(12, result.Damage.HpAfter);
        Assert.False(result.Damage.HpDamageApplied);
        Assert.Equal(0, fixture.HpDamageEngine.Calls);
        Assert.Equal(2, fixture.DiceRoller.PercentileCalls);
        Assert.Equal(DamageDispositionStatus.Consumed, result.State.Combat!.DamageDispositions[entry.Key].Status);
    }

    [Fact]
    public async Task ResolveCombatDamage_OpponentVitalityFloorsAtZeroPreservesProfileAndTerminatesOnlyAfterLastOpponent()
    {
        var fixture = await CreateCombatDamageGameAsync([3, 3], [40, 100, 40, 100]);
        var opponents = new[]
        {
            Opponent("First", 70) with { CurrentHp = 2, MaxHp = 2 },
            Opponent("Second", 60) with { CurrentHp = 2, MaxHp = 2 }
        };
        var firstPending = await CreatePendingOpponentDamageDispositionAsync(fixture, opponents, "opponent:0");
        var firstEntry = Assert.Single(firstPending.Combat!.DamageDispositions);
        var firstProfile = firstPending.Combat.Participants.Single(
            participant => participant.ParticipantId == firstEntry.Value.Disposition.TargetParticipantId).DamageProfile;

        var firstResult = await ResolveCombatDamageAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            firstPending.Revision,
            firstEntry.Key);

        var firstSession = firstResult.State!.Combat!;
        var defeatedFirst = firstSession.Participants.Single(participant => participant.ParticipantId.Value == "opponent:0");
        Assert.Equal(new OpponentVitalityState(0, 2), defeatedFirst.OpponentVitality);
        Assert.False(defeatedFirst.Active);
        Assert.Equal(firstProfile, defeatedFirst.DamageProfile);
        Assert.True(firstSession.Active);
        Assert.Equal("opponent:1", firstSession.Order[firstSession.TurnIndex].Value);
        Assert.Equal(0, fixture.HpDamageEngine.Calls);
        Assert.Equal(2, fixture.DiceRoller.PercentileCalls);

        var passed = await PassCombatTurnAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            firstResult.State.Revision);
        var secondPending = await BeginAndResolveDamageDispositionAsync(
            fixture,
            passed.State!,
            fixture.HostId,
            $"character:{fixture.HostCharacterId}",
            "opponent:1");
        var secondEntry = secondPending.Combat!.DamageDispositions.Single(
            pair => pair.Value.Status == DamageDispositionStatus.Pending);
        var secondResult = await ResolveCombatDamageAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            secondPending.Revision,
            secondEntry.Key);

        Assert.False(secondResult.State!.Combat!.Active);
        Assert.Equal("opposition_defeated", secondResult.State.Combat.EndReason);
        Assert.Equal(new OpponentVitalityState(0, 2), secondResult.State.Combat.Participants.Single(
            participant => participant.ParticipantId.Value == "opponent:1").OpponentVitality);
        Assert.Equal(0, fixture.HpDamageEngine.Calls);
        Assert.Equal(4, fixture.DiceRoller.PercentileCalls);
    }

    [Fact]
    public async Task ResolveCombatDamage_OpponentZeroNetPreservesCombatVitalityWithoutHpOrConRoll()
    {
        var fixture = await CreateCombatDamageGameAsync([2], [40, 100]);
        var opponent = Opponent("Armored", 70) with
        {
            CurrentHp = 5,
            MaxHp = 5,
            FixedArmor = 2
        };
        var pendingState = await CreatePendingOpponentDamageDispositionAsync(
            fixture,
            [opponent],
            "opponent:0");
        var entry = Assert.Single(pendingState.Combat!.DamageDispositions);
        var before = pendingState.Combat.Participants.Single(
            participant => participant.ParticipantId.Value == "opponent:0");

        var result = await ResolveCombatDamageAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            pendingState.Revision,
            entry.Key);

        var after = result.State!.Combat!.Participants.Single(
            participant => participant.ParticipantId.Value == "opponent:0");
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Damage!.NetDamage);
        Assert.False(result.Damage.HpDamageApplied);
        Assert.False(result.Damage.TargetDefeated);
        Assert.Equal(before.OpponentVitality, after.OpponentVitality);
        Assert.Equal(before.DamageProfile, after.DamageProfile);
        Assert.True(after.Active);
        Assert.Equal(0, fixture.HpDamageEngine.Calls);
        Assert.Equal(2, fixture.DiceRoller.PercentileCalls);
        Assert.Equal(DamageDispositionStatus.Consumed, result.State.Combat.DamageDispositions[entry.Key].Status);
    }

    [Fact]
    public async Task CombatDamageOrder_InactiveCurrentSlotScansSameIndexWithoutRoundWrap()
    {
        var fixture = await CreateCombatDamageGameAsync([3]);
        var pendingState = await CreatePendingOpponentDamageDispositionAsync(
            fixture,
            [
                Opponent("Target", 70) with { CurrentHp = 2, MaxHp = 2 },
                Opponent("Next", 60)
            ],
            "opponent:0");
        Assert.Equal("opponent:0", pendingState.Combat!.Order[pendingState.Combat.TurnIndex].Value);
        var entry = Assert.Single(pendingState.Combat.DamageDispositions);

        var result = await ResolveCombatDamageAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            pendingState.Revision,
            entry.Key);

        Assert.Equal(pendingState.Combat.Round, result.State!.Combat!.Round);
        Assert.Equal("opponent:1", result.State.Combat.Order[result.State.Combat.TurnIndex].Value);
        Assert.Equal(pendingState.Combat.ActionCounts, result.State.Combat.ActionCounts);
        Assert.Equal(pendingState.Combat.ResponseCounts, result.State.Combat.ResponseCounts);
    }

    [Fact]
    public async Task CombatDamageOrder_AlreadyWrappedOpposedResolveDoesNotWrapOrResetAgain()
    {
        var fixture = await CreateCombatDamageGameAsync([3], [40, 100]);
        var opponents = new[]
        {
            Opponent("Target", 90) with { CurrentHp = 2, MaxHp = 2 },
            Opponent("Other", 85)
        };
        var started = await StartCombatAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            1,
            [fixture.HostCharacterId],
            opponents);
        var afterFirstPass = await PassCombatTurnAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            started.State!.Revision);
        var afterSecondPass = await PassCombatTurnAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            afterFirstPass.State!.Revision);
        var pendingState = await BeginAndResolveDamageDispositionAsync(
            fixture,
            afterSecondPass.State!,
            fixture.HostId,
            $"character:{fixture.HostCharacterId}",
            "opponent:0");
        Assert.Equal(2, pendingState.Combat!.Round);
        Assert.Equal("opponent:0", pendingState.Combat.Order[pendingState.Combat.TurnIndex].Value);
        Assert.Empty(pendingState.Combat.ActionCounts);
        Assert.Empty(pendingState.Combat.ResponseCounts);
        var entry = Assert.Single(pendingState.Combat.DamageDispositions);

        var result = await ResolveCombatDamageAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            pendingState.Revision,
            entry.Key);

        Assert.Equal(2, result.State!.Combat!.Round);
        Assert.Equal("opponent:1", result.State.Combat.Order[result.State.Combat.TurnIndex].Value);
        Assert.Empty(result.State.Combat.ActionCounts);
        Assert.Empty(result.State.Combat.ResponseCounts);
        Assert.Equal(2, fixture.DiceRoller.PercentileCalls);
    }

    [Fact]
    public async Task CombatDamageOrder_RepairAtEndUsesCanonicalWrapExactlyOnce()
    {
        var fixture = await CreateCombatDamageGameAsync([3], [40, 100]);
        var opponents = new[]
        {
            Opponent("Earlier", 90),
            Opponent("Target", 70) with { CurrentHp = 2, MaxHp = 2 }
        };
        var started = await StartCombatAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            1,
            [fixture.HostCharacterId],
            opponents);
        var afterPass = await PassCombatTurnAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            started.State!.Revision);
        var pendingState = await BeginAndResolveDamageDispositionAsync(
            fixture,
            afterPass.State!,
            fixture.HostId,
            $"character:{fixture.HostCharacterId}",
            "opponent:1");
        Assert.Equal(1, pendingState.Combat!.Round);
        Assert.Equal("opponent:1", pendingState.Combat.Order[pendingState.Combat.TurnIndex].Value);
        Assert.NotEmpty(pendingState.Combat.ActionCounts);
        Assert.NotEmpty(pendingState.Combat.ResponseCounts);
        var entry = Assert.Single(pendingState.Combat.DamageDispositions);

        var result = await ResolveCombatDamageAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            pendingState.Revision,
            entry.Key);

        Assert.Equal(2, result.State!.Combat!.Round);
        Assert.Equal("opponent:0", result.State.Combat.Order[result.State.Combat.TurnIndex].Value);
        Assert.Empty(result.State.Combat.ActionCounts);
        Assert.Empty(result.State.Combat.ResponseCounts);
        Assert.Equal(2, fixture.DiceRoller.PercentileCalls);
    }

    [Fact]
    public async Task ResolveCombatDamage_PostWrapDyingRngStoreFailureAlsoProhibitsReroll()
    {
        var fixture = await CreateCombatDamageGameAsync([], [1, 100, 100]);
        ReplaceCharacterHealth(
            fixture.StateStore,
            fixture.Room.RoomId,
            fixture.HostCharacterId,
            new CharacterHealthState(
                0,
                12,
                60,
                majorWound: true,
                unconscious: true,
                dyingEpisode: new DyingEpisodeState("prior", [], 1, false),
                stabilized: null,
                deadCondition: null,
                treatmentHistory: [],
                history: [],
                lastDamageEvent: null));
        var started = await StartCombatAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            1,
            [fixture.HostCharacterId],
            [
                Opponent("Earlier", 90),
                Opponent("Target", 70) with { CurrentHp = 2, MaxHp = 2 }
            ]);
        var state = started.State!;
        for (var index = 0; index < 4; index++)
        {
            var passed = await PassCombatTurnAsync(
                fixture.Coordinator,
                fixture.Room.RoomId,
                fixture.HostId,
                state.Revision);
            Assert.True(passed.IsSuccess);
            state = passed.State!;
        }

        Assert.Equal(2, state.Combat!.Round);
        Assert.Equal($"character:{fixture.HostCharacterId}", state.Combat.Order[state.Combat.TurnIndex].Value);
        var pendingState = await BeginAndResolveDamageDispositionAsync(
            fixture,
            state,
            fixture.HostId,
            $"character:{fixture.HostCharacterId}",
            "opponent:1");
        var entry = Assert.Single(pendingState.Combat!.DamageDispositions);
        Assert.Equal(CombatDamageMode.InitiatorExtremeEligible, entry.Value.Disposition.Mode);
        Assert.Equal(0, fixture.DiceRoller.GenericCalls);
        Assert.Equal(2, fixture.DiceRoller.PercentileCalls);
        fixture.StateStore.ArmConsumptionFailure(() => true);

        var exception = await Record.ExceptionAsync(() => ResolveCombatDamageAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            pendingState.Revision,
            entry.Key));

        Assert.NotNull(exception);
        Assert.Equal("CombatDamageCommitInvariantException", exception.GetType().Name);
        Assert.Contains("must not be re-rolled", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, fixture.DiceRoller.GenericCalls);
        Assert.Equal(3, fixture.DiceRoller.PercentileCalls);
        Assert.Same(pendingState, GetRequiredState(fixture.StateStore, fixture.Room.RoomId));
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
            ReplaceDamageDispositions(
                fixture,
                state.Combat!.DamageDispositions.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Key == exchange.ExchangeId
                        ? ConsumedDisposition(
                            pair.Key,
                            pair.Value.Disposition.OwnerParticipantId,
                            pair.Value.Disposition.TargetParticipantId,
                            pair.Value.Disposition.CreatedGameRevision)
                        : pair.Value));
            Assert.True(fixture.StateStore.TryGet(fixture.Room.RoomId, out var storedState));
            state = Assert.IsType<MultiplayerGameState>(storedState);
        }

        var finalSession = state.Combat!;
        Assert.Equal(120, finalSession.History.Count);
        Assert.NotEqual(firstExchangeId, finalSession.History[0].ExchangeId);
        Assert.Equal(finalSession.LastExchange!.ExchangeId, finalSession.History[^1].ExchangeId);
        Assert.True(finalSession.DamageDispositions.ContainsKey(firstExchangeId!));
        Assert.Equal(121, finalSession.DamageDispositions.Count);
        Assert.All(finalSession.DamageDispositions.Values, entry => Assert.Equal(DamageDispositionStatus.Consumed, entry.Status));
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
    public async Task Projection_CombatViewerActions_OwnCurrentInvestigatorGetsAttackPassAndActiveOpponents()
    {
        var fixture = await CreateCombatGameAsync();
        var started = await StartCombatAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            1,
            [fixture.HostCharacterId, fixture.MemberCharacterId],
            [Opponent("Cultist", 70)]);
        Assert.True(started.IsSuccess);

        var currentCharacterId = fixture.HostCharacterId;
        var actions = Assert.IsType<CombatViewerActionsSnapshot>(
            GameProjection.Build(started.State!, fixture.HostId).Combat!.ViewerActions);

        Assert.Equal(currentCharacterId, actions.ActorCharacterId);
        Assert.True(actions.CanMeleeAttack);
        Assert.True(actions.CanPass);
        Assert.Equal(["opponent:0"], actions.EligibleTargetParticipantIds);
        Assert.Null(actions.PendingResponse);
    }

    [Fact]
    public async Task Projection_CombatViewerActions_InactiveCombatHasNoActionableAffordance()
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
        var startedState = started.State!;
        var endedState = new MultiplayerGameState(
            startedState.RoomId,
            startedState.Revision,
            startedState.Status,
            startedState.CreatedAt,
            startedState.Characters,
            startedState.LastCheck,
            startedState.Combat! with { Active = false });

        var combat = Assert.IsType<CombatSnapshot>(GameProjection.Build(endedState, fixture.HostId).Combat);

        Assert.Null(combat.ViewerActions);
    }

    [Fact]
    public async Task Projection_CombatViewerActions_NonCurrentOwnerGetsNoActorActions()
    {
        var fixture = await CreateCombatGameAsync();
        var started = await StartCombatAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            1,
            [fixture.HostCharacterId, fixture.MemberCharacterId],
            [Opponent("Cultist", 70)]);
        Assert.True(started.IsSuccess);

        var combat = Assert.IsType<CombatSnapshot>(GameProjection.Build(started.State!, fixture.MemberId).Combat);

        Assert.Null(combat.ViewerActions);
    }

    [Fact]
    public async Task Projection_CombatViewerActions_ExactHumanDefenderAloneGetsPendingResponse()
    {
        var fixture = await CreateResolvableCombatGameAsync([1, 100]);
        var pending = await StartAndBeginAgainstPlayerAsync(fixture);
        Assert.True(pending.IsSuccess);

        var defenderActions = Assert.IsType<CombatViewerActionsSnapshot>(
            GameProjection.Build(pending.State!, fixture.HostId).Combat!.ViewerActions);
        var expectedPending = Assert.IsType<PendingCombatExchange>(pending.State!.Combat!.PendingExchange);

        Assert.Equal(expectedPending.ExchangeId, defenderActions.PendingResponse!.ExchangeId);
        Assert.Equal(["dodge", "fight_back"], defenderActions.PendingResponse.AvailableResponses);
    }

    [Fact]
    public async Task Projection_CombatViewerActions_AttackerAndOtherParticipantDoNotGetExchangeOrResponses()
    {
        var fixture = await CreateCombatGameAsync();
        var started = await StartCombatAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            1,
            [fixture.HostCharacterId, fixture.MemberCharacterId],
            [Opponent("Cultist", 70)]);
        var pending = await BeginOpposedExchangeAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            started.State!.Revision,
            "character:" + fixture.HostCharacterId,
            "opponent:0");
        Assert.True(pending.IsSuccess);

        var attackerActions = GameProjection.Build(pending.State!, fixture.HostId).Combat!.ViewerActions;
        var otherParticipantActions = GameProjection.Build(pending.State!, fixture.MemberId).Combat!.ViewerActions;

        Assert.Null(attackerActions);
        Assert.Null(otherParticipantActions);
    }

    [Fact]
    public async Task Projection_CombatViewerActions_PendingDamageSuppressesAttackAndPass()
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
        ReplaceDamageDispositions(
            fixture,
            new Dictionary<string, DamageDispositionState>
            {
                ["pending-damage"] = PendingDisposition(
                    "pending-damage",
                    new CombatParticipantId("character:" + fixture.HostCharacterId),
                    new CombatParticipantId("opponent:0"),
                    started.State!.Revision)
            });

        var state = GetRequiredState(fixture.StateStore, fixture.Room.RoomId);

        Assert.Null(GameProjection.Build(state, fixture.HostId).Combat!.ViewerActions);
    }

    [Fact]
    public async Task Projection_CombatViewerActions_NonparticipantStillGetsNullCombat()
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

        Assert.Null(GameProjection.Build(started.State!, fixture.MemberId).Combat);
    }

    [Fact]
    public async Task Projection_CombatViewerActions_JsonHasExactSafePropertySetAndNoInternalFields()
    {
        var fixture = await CreateResolvableCombatGameAsync([1, 100]);
        var pending = await StartAndBeginAgainstPlayerAsync(fixture);
        Assert.True(pending.IsSuccess);

        var actions = Assert.IsType<CombatViewerActionsSnapshot>(
            GameProjection.Build(pending.State!, fixture.HostId).Combat!.ViewerActions);
        using var actionDocument = JsonDocument.Parse(JsonSerializer.Serialize(
            actions,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        using var pendingDocument = JsonDocument.Parse(JsonSerializer.Serialize(
            actions.PendingResponse,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.Equal(
            ["actorCharacterId", "canMeleeAttack", "canPass", "eligibleTargetParticipantIds", "pendingResponse"],
            actionDocument.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
        Assert.Equal(
            ["availableResponses", "exchangeId"],
            pendingDocument.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
        var json = actionDocument.RootElement.GetRawText();
        foreach (var forbidden in new[]
                 {
                      "policy", "allowance", "roll", "rawTarget", "stat", "registry", "schedule", "history", "source", "provenance"
                 })
        {
            Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
        }
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
        Assert.DoesNotContain("DamageDispositions", combatJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ResponseAllowance", combatJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ResponsePolicy", combatJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NpcResponsePolicy", combatJson, StringComparison.OrdinalIgnoreCase);
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
    public async Task Projection_ConsumedDamageExposesOnlySafeLatestSummaryAndExistingOwnHealth()
    {
        var fixture = await CreateCombatDamageGameAsync(
            [],
            [1, 100],
            hostLoadout: new CharacterCombatLoadout(
                Weapon("private-maul", "秘密巨锤", "1d12", addsDamageBonus: false),
                0));
        var opponent = Opponent("Cultist", 70) with
        {
            Str = 91,
            Siz = 82,
            CurrentHp = 3,
            MaxHp = 13,
            FixedArmor = 9,
            Weapon = Weapon("private-claw", "隐藏利爪", "1d4")
        };
        var started = await StartCombatAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            1,
            [fixture.HostCharacterId, fixture.MemberCharacterId],
            [opponent]);
        var pending = await BeginOpposedExchangeAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            started.State!.Revision,
            "character:" + fixture.HostCharacterId,
            "opponent:0");
        var resolved = await ResolvePendingExchangeAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            null,
            pending.State!.Revision,
            pending.State.Combat!.PendingExchange!.ExchangeId,
            CombatResponse.Dodge);
        var disposition = Assert.Single(resolved.State!.Combat!.DamageDispositions);

        var consumed = await ResolveCombatDamageAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            resolved.State.Revision,
            disposition.Key);

        Assert.True(consumed.IsSuccess);
        var committedState = Assert.IsType<MultiplayerGameState>(consumed.State);
        var committedDamage = Assert.IsType<CombatDamageResult>(consumed.Damage);
        var olderExchangeId = "older-consumed-damage";
        var olderResult = committedDamage with
        {
            ExchangeId = olderExchangeId,
            NetDamage = 99,
            TargetDefeated = false,
            ResolvedAt = committedDamage.ResolvedAt.AddMinutes(-1)
        };
        var olderDisposition = new DamageDispositionState(
            new DamageDispositionData(
                olderExchangeId,
                olderResult.OwnerParticipantId,
                olderResult.TargetParticipantId,
                olderResult.DamageMode,
                committedState.Revision - 1),
            DamageDispositionStatus.Consumed,
            olderResult);
        var consumedState = new MultiplayerGameState(
            committedState.RoomId,
            committedState.Revision,
            committedState.Status,
            committedState.CreatedAt,
            committedState.Characters,
            committedState.LastCheck,
            committedState.Combat! with
            {
                DamageDispositions = committedState.Combat.DamageDispositions
                    .Append(new KeyValuePair<string, DamageDispositionState>(olderExchangeId, olderDisposition))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            });
        var hostProjection = GameProjection.Build(consumedState, fixture.HostId);
        var memberProjection = GameProjection.Build(consumedState, fixture.MemberId);
        var nonparticipantProjection = GameProjection.Build(consumedState, Guid.NewGuid());
        var expectedDamage = new CombatDamageSnapshot(
            disposition.Key,
            "character:" + fixture.HostCharacterId,
            "opponent:0",
            "applied",
            3,
            true);
        Assert.Equal(expectedDamage, hostProjection.Combat!.LastDamage);
        Assert.Equal(expectedDamage, memberProjection.Combat!.LastDamage);
        Assert.Null(nonparticipantProjection.Combat);
        Assert.NotNull(hostProjection.Characters.Single(character => character.CharacterId == fixture.HostCharacterId).Health);
        Assert.Null(hostProjection.Characters.Single(character => character.CharacterId == fixture.MemberCharacterId).Health);
        Assert.Null(memberProjection.Characters.Single(character => character.CharacterId == fixture.HostCharacterId).Health);
        Assert.NotNull(memberProjection.Characters.Single(character => character.CharacterId == fixture.MemberCharacterId).Health);

        var json = JsonSerializer.Serialize(hostProjection);
        using var document = JsonDocument.Parse(json);
        var combat = document.RootElement.GetProperty("Combat");
        var damageProperties = combat.GetProperty("LastDamage")
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            new[] { "ExchangeId", "NetDamage", "Outcome", "OwnerParticipantId", "TargetDefeated", "TargetParticipantId" }
                .OrderBy(name => name, StringComparer.Ordinal),
            damageProperties);
        var opponentProperties = combat.GetProperty("Participants")
            .EnumerateArray()
            .Single(participant => participant.GetProperty("ParticipantId").GetString() == "opponent:0")
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            new[] { "Active", "CharacterId", "Current", "Label", "ParticipantId", "Side", "Stats", "ViewerOwned" }
                .OrderBy(name => name, StringComparer.Ordinal),
            opponentProperties);
        foreach (var forbiddenProperty in new[]
                 {
                     "WeaponResult", "DamageBonusResult", "RawRolls", "DamageMode", "WeaponId", "WeaponExpression",
                     "GrossDamage", "Armor", "HpBefore", "HpAfter", "HpDamageApplied", "ResolvedAt", "Str", "Siz",
                     "DamageBonus", "DamageProfile", "OpponentVitality", "DamageDispositions", "DamageDisposition",
                     "CombatDamageResult", "EventKey", "History", "SourceId", "Provenance"
                 })
        {
            Assert.DoesNotContain($"\"{forbiddenProperty}\"", json, StringComparison.Ordinal);
        }
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

    private static async Task<CombatDamageGameFixture> CreateCombatDamageGameAsync(
        IReadOnlyList<int> genericRolls,
        IReadOnlyList<int>? percentileRolls = null,
        CharacterHealthSetup? hostHealth = null,
        CharacterHealthSetup? memberHealth = null,
        CharacterCombatLoadout? hostLoadout = null)
    {
        var roomStore = new InMemoryRoomStore();
        var stateStore = new ControlledGameStateStore();
        var hostId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var room = CreateRoom(roomStore, hostId, "Host");
        Assert.True((await new RoomCoordinator(roomStore).JoinAsync(
            new JoinRoomCommand(room.RoomId, memberId, "Member"))).IsSuccess);
        var diceRoller = new SequenceDiceRoller(percentileRolls ?? [40, 100], genericRolls);
        var hpDamageEngine = new TrackingHpDamageEngine();
        var notifier = new TrackingGameRealtimeNotifier();
        var coordinator = new GameCoordinator(
            roomStore,
            stateStore,
            diceRoller,
            new CocCheckResolutionEngine(),
            hpDamageEngine,
            new CocHealthStabilizationEngine(),
            notifier);
        var initialized = await coordinator.InitializeAsync(new InitializeGameCommand(
            room.RoomId,
            hostId,
            [
                new InitializeCharacterCommand(hostId, "Host", CombatValues(80, 55, 45), hostHealth ?? Health()),
                new InitializeCharacterCommand(memberId, "Member", CombatValues(70, 65, 50), memberHealth ?? Health())
            ]));

        Assert.True(initialized.IsSuccess);
        if (hostLoadout is not null)
        {
            var state = GetRequiredState(stateStore, room.RoomId);
            var hostCharacterId = state.Characters.Single(character => character.OwnerPlayerId == hostId).CharacterId;
            var replacement = new MultiplayerGameState(
                state.RoomId,
                state.Revision,
                state.Status,
                state.CreatedAt,
                state.Characters.Select(character => character.CharacterId == hostCharacterId
                    ? new CharacterState(
                        character.CharacterId,
                        character.OwnerPlayerId,
                        character.Name,
                        character.CheckValues,
                        character.Health,
                        hostLoadout)
                    : character),
                state.LastCheck,
                state.Combat);
            Assert.True(stateStore.TryReplace(state, replacement));
        }

        var initializedState = GetRequiredState(stateStore, room.RoomId);
        return new CombatDamageGameFixture(
            coordinator,
            room,
            hostId,
            memberId,
            initializedState.Characters.Single(character => character.OwnerPlayerId == hostId).CharacterId,
            initializedState.Characters.Single(character => character.OwnerPlayerId == memberId).CharacterId,
            diceRoller,
            stateStore,
            hpDamageEngine,
            notifier);
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

    private sealed record CombatDamageGameFixture(
        GameCoordinator Coordinator,
        RoomSession Room,
        Guid HostId,
        Guid MemberId,
        Guid HostCharacterId,
        Guid MemberCharacterId,
        CountingDiceRoller DiceRoller,
        ControlledGameStateStore StateStore,
        TrackingHpDamageEngine HpDamageEngine,
        TrackingGameRealtimeNotifier Notifier);

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

    private static void ReplaceCharacterCombatProfile(
        CombatGameFixture fixture,
        Guid characterId,
        IReadOnlyDictionary<string, int> checkValues,
        CharacterCombatLoadout combatLoadout)
    {
        Assert.True(fixture.StateStore.TryGet(fixture.Room.RoomId, out var state));
        var replacement = new MultiplayerGameState(
            state!.RoomId,
            state.Revision,
            state.Status,
            state.CreatedAt,
            state.Characters.Select(character => character.CharacterId == characterId
                ? new CharacterState(
                    character.CharacterId,
                    character.OwnerPlayerId,
                    character.Name,
                    checkValues,
                    character.Health,
                    combatLoadout)
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

    private static void ReplaceCombatParticipant(
        IGameStateStore stateStore,
        Guid roomId,
        string participantId,
        CombatParticipantState replacementParticipant)
    {
        var state = GetRequiredState(stateStore, roomId);
        var replacement = new MultiplayerGameState(
            state.RoomId,
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
        Assert.True(stateStore.TryReplace(state, replacement));
    }

    private static void ReplaceCharacterHealth(
        IGameStateStore stateStore,
        Guid roomId,
        Guid characterId,
        CharacterHealthState health)
    {
        var state = GetRequiredState(stateStore, roomId);
        var replacement = new MultiplayerGameState(
            state.RoomId,
            state.Revision,
            state.Status,
            state.CreatedAt,
            state.Characters.Select(character => character.CharacterId == characterId
                ? character.WithHealth(health)
                : character),
            state.LastCheck,
            state.Combat);
        Assert.True(stateStore.TryReplace(state, replacement));
    }

    private static void ReplaceDamageDispositions(
        CombatGameFixture fixture,
        IReadOnlyDictionary<string, DamageDispositionState> damageDispositions)
    {
        Assert.True(fixture.StateStore.TryGet(fixture.Room.RoomId, out var state));
        var replacement = new MultiplayerGameState(
            state!.RoomId,
            state.Revision,
            state.Status,
            state.CreatedAt,
            state.Characters,
            state.LastCheck,
            state.Combat! with { DamageDispositions = damageDispositions });
        Assert.True(fixture.StateStore.TryReplace(state, replacement));
    }

    private static void ReplaceDamageDispositions(
        IGameStateStore stateStore,
        Guid roomId,
        IReadOnlyDictionary<string, DamageDispositionState> damageDispositions)
    {
        var state = GetRequiredState(stateStore, roomId);
        var replacement = new MultiplayerGameState(
            state.RoomId,
            state.Revision,
            state.Status,
            state.CreatedAt,
            state.Characters,
            state.LastCheck,
            state.Combat! with { DamageDispositions = damageDispositions });
        Assert.True(stateStore.TryReplace(state, replacement));
    }

    private static MultiplayerGameState GetRequiredState(IGameStateStore stateStore, Guid roomId)
    {
        Assert.True(stateStore.TryGet(roomId, out var state));
        return Assert.IsType<MultiplayerGameState>(state);
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
        new Dictionary<string, DamageDispositionState>(),
        new Dictionary<Guid, DyingScheduleState>(),
        DateTimeOffset.UtcNow,
        null,
        null);

    private static Dictionary<string, int> Values() => new() { ["spotHidden"] = 60 };

    private static Dictionary<string, int> CombatValues(
        int dex,
        int fighting,
        int dodge,
        int str = 60,
        int siz = 50) => new()
        {
            ["dex"] = dex,
            ["fighting_brawl"] = fighting,
            ["dodge"] = dodge,
            ["str"] = str,
            ["siz"] = siz
        };

    private static CombatOpponent Opponent(string label, int dex) => new(
        label,
        dex,
        55,
        40,
        [CombatResponse.Dodge, CombatResponse.FightBack],
        1,
        CombatResponse.Dodge,
        60,
        50,
        10,
        10,
        0,
        Weapon("unarmed", "徒手/拳脚", "1d3"));

    private static CharacterCombatLoadout DefaultLoadout() => new(
        Weapon("unarmed", "徒手/拳脚", "1d3"),
        0);

    private static CombatWeaponProfile Weapon(
        string weaponId,
        string label,
        string expression,
        bool addsDamageBonus = true) =>
        CocCombatDamageRules.NormalizeWeapon(
            weaponId,
            label,
            expression,
            addsDamageBonus,
            "melee_non_impaling");

    private static DamageDispositionState PendingDisposition(
        string exchangeId,
        CombatParticipantId ownerParticipantId,
        CombatParticipantId targetParticipantId,
        long createdGameRevision) => new(
            new DamageDispositionData(
                exchangeId,
                ownerParticipantId,
                targetParticipantId,
                CombatDamageMode.Regular,
                createdGameRevision),
            DamageDispositionStatus.Pending,
            null);

    private static DamageDispositionState ConsumedDisposition(
        string exchangeId,
        CombatParticipantId ownerParticipantId,
        CombatParticipantId targetParticipantId,
        long createdGameRevision)
    {
        var disposition = new DamageDispositionData(
            exchangeId,
            ownerParticipantId,
            targetParticipantId,
            CombatDamageMode.Regular,
            createdGameRevision);
        return new DamageDispositionState(
            disposition,
            DamageDispositionStatus.Consumed,
            new CombatDamageResult(
                exchangeId,
                ownerParticipantId,
                targetParticipantId,
                CombatDamageMode.Regular,
                CombatDamageOutcome.Applied,
                "test-weapon",
                "1",
                null,
                null,
                1,
                0,
                1,
                10,
                9,
                true,
                false,
                DateTimeOffset.UnixEpoch));
    }

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

    private static async Task<InternalCombatResult> ResolveCombatDamageAsync(
        GameCoordinator coordinator,
        Guid roomId,
        long expectedRevision,
        string exchangeId) =>
        await InvokeInternalCombatAsync(
            coordinator,
            "ResolveCombatDamageAsync",
            roomId,
            expectedRevision,
            exchangeId);

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

    private static async Task<MultiplayerGameState> CreatePendingDamageDispositionAsync(
        CombatDamageGameFixture fixture)
    {
        var started = await StartCombatAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            1,
            [fixture.HostCharacterId],
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
        var exchange = Assert.IsType<PendingCombatExchange>(pending.State!.Combat!.PendingExchange);
        var resolved = await ResolvePendingExchangeAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            null,
            pending.State.Revision,
            exchange.ExchangeId,
            CombatResponse.Dodge);
        Assert.True(resolved.IsSuccess);
        Assert.Equal(DamageDispositionStatus.Pending, Assert.Single(resolved.State!.Combat!.DamageDispositions).Value.Status);
        fixture.Notifier.Reset();
        return resolved.State;
    }

    private static async Task<MultiplayerGameState> CreatePendingInvestigatorDamageDispositionAsync(
        CombatDamageGameFixture fixture,
        CombatOpponent opponent,
        IReadOnlyList<Guid>? characterIds = null)
    {
        var state = GetRequiredState(fixture.StateStore, fixture.Room.RoomId);
        var started = await StartCombatAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            state.Revision,
            characterIds ?? [fixture.HostCharacterId],
            [opponent]);
        Assert.True(started.IsSuccess);
        return await BeginAndResolveDamageDispositionAsync(
            fixture,
            started.State!,
            fixture.HostId,
            "opponent:0",
            $"character:{fixture.HostCharacterId}");
    }

    private static async Task<MultiplayerGameState> CreatePendingOpponentDamageDispositionAsync(
        CombatDamageGameFixture fixture,
        IReadOnlyList<CombatOpponent> opponents,
        string targetParticipantId)
    {
        var state = GetRequiredState(fixture.StateStore, fixture.Room.RoomId);
        var started = await StartCombatAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            fixture.HostId,
            state.Revision,
            [fixture.HostCharacterId],
            opponents);
        Assert.True(started.IsSuccess);
        return await BeginAndResolveDamageDispositionAsync(
            fixture,
            started.State!,
            fixture.HostId,
            $"character:{fixture.HostCharacterId}",
            targetParticipantId);
    }

    private static async Task<MultiplayerGameState> BeginAndResolveDamageDispositionAsync(
        CombatDamageGameFixture fixture,
        MultiplayerGameState state,
        Guid requestingPlayerId,
        string attackerParticipantId,
        string defenderParticipantId)
    {
        var pending = await BeginOpposedExchangeAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            requestingPlayerId,
            state.Revision,
            attackerParticipantId,
            defenderParticipantId);
        Assert.True(pending.IsSuccess);
        var exchange = Assert.IsType<PendingCombatExchange>(pending.State!.Combat!.PendingExchange);
        var resolved = await ResolvePendingExchangeAsync(
            fixture.Coordinator,
            fixture.Room.RoomId,
            exchange.DefenderOwnerPlayerId,
            pending.State.Revision,
            exchange.ExchangeId,
            CombatResponse.Dodge);
        Assert.True(resolved.IsSuccess);
        Assert.Equal(
            DamageDispositionStatus.Pending,
            resolved.State!.Combat!.DamageDispositions[exchange.ExchangeId].Status);
        fixture.Notifier.Reset();
        return resolved.State;
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
        var changed = Assert.IsType<bool>(result.GetType().GetProperty("Changed")!.GetValue(result));
        var error = result.GetType().GetProperty("Error")!.GetValue(result) as GameError;
        var value = result.GetType().GetProperty("Value")!.GetValue(result);
        var state = value?.GetType().GetProperty("State")!.GetValue(value) as MultiplayerGameState;
        var damage = value?.GetType().GetProperty("Damage")?.GetValue(value) as CombatDamageResult;
        return new InternalCombatResult(isSuccess, error?.Code, changed, state, damage);
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
                opponent.NpcResponsePolicy,
                opponent.Str,
                opponent.Siz,
                opponent.CurrentHp,
                opponent.MaxHp,
                opponent.FixedArmor,
                opponent.Weapon), index);
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

        public int GenericCalls { get; private set; }

        public PercentileDiceRoll RollPercentile(int bonusDice, int penaltyDice)
        {
            PercentileCalls++;
            return Roll(bonusDice, penaltyDice);
        }

        public GenericDiceRoll RollDice(DiceRollRequest request)
        {
            GenericCalls++;
            return RollGeneric(request);
        }

        protected abstract PercentileDiceRoll Roll(int bonusDice, int penaltyDice);

        protected abstract GenericDiceRoll RollGeneric(DiceRollRequest request);
    }

    private sealed class ThrowingDiceRoller : CountingDiceRoller
    {
        protected override PercentileDiceRoll Roll(int bonusDice, int penaltyDice)
        {
            throw new InvalidOperationException("Combat Start and Begin must not roll dice.");
        }

        protected override GenericDiceRoll RollGeneric(DiceRollRequest request)
        {
            throw new InvalidOperationException("Combat Start and Begin must not roll dice.");
        }
    }

    private sealed class SequenceDiceRoller(
        IEnumerable<int> selectedRolls,
        IEnumerable<int>? genericRolls = null) : CountingDiceRoller
    {
        private readonly Queue<int> selectedRolls = new(selectedRolls);
        private readonly Queue<int> genericRolls = new(genericRolls ?? []);

        protected override PercentileDiceRoll Roll(int bonusDice, int penaltyDice)
        {
            if (!selectedRolls.TryDequeue(out var roll))
            {
                throw new InvalidOperationException("No deterministic combat roll remains.");
            }

            return new PercentileDiceRoll(roll, [roll]);
        }

        protected override GenericDiceRoll RollGeneric(DiceRollRequest request)
        {
            var rawRolls = new int[request.Count];
            var total = 0;
            for (var index = 0; index < rawRolls.Length; index++)
            {
                if (!genericRolls.TryDequeue(out var roll))
                {
                    throw new InvalidOperationException("No deterministic generic roll remains.");
                }

                rawRolls[index] = roll;
                total += roll;
            }

            return new GenericDiceRoll(request.Count, request.Faces, Array.AsReadOnly(rawRolls), total);
        }
    }

    private sealed class TrackingHpDamageEngine : IHpDamageEngine
    {
        private readonly CocHpDamageEngine inner = new();

        public int Calls { get; private set; }

        public HpDamageInput? LastInput { get; private set; }

        public HpDamageResolutionResult Apply(CharacterHealthState state, HpDamageInput input)
        {
            Calls++;
            LastInput = input;
            return inner.Apply(state, input);
        }
    }

    private sealed class TrackingGameRealtimeNotifier : IGameRealtimeNotifier
    {
        public int GameSnapshotCalls { get; private set; }

        public Task PublishGameSnapshotAsync(Guid roomId)
        {
            GameSnapshotCalls++;
            return Task.CompletedTask;
        }

        public Task PublishCheckResolvedAsync(Guid roomId, CheckResolvedEvent message) => Task.CompletedTask;

        public Task SendGameSnapshotAsync(string connectionId, Guid roomId, Guid playerId) => Task.CompletedTask;

        public void Reset() => GameSnapshotCalls = 0;
    }

    private sealed class ControlledGameStateStore : IGameStateStore
    {
        private readonly InMemoryGameStateStore inner = new();
        private Func<bool>? failConsumptionReplacement;

        public int ReplacementAttempts { get; private set; }

        public int ConsumptionReplacementAttempts { get; private set; }

        public bool TryAdd(MultiplayerGameState state) => inner.TryAdd(state);

        public bool TryGet(Guid roomId, out MultiplayerGameState? state) => inner.TryGet(roomId, out state);

        public bool TryReplace(MultiplayerGameState expectedState, MultiplayerGameState replacementState)
        {
            ReplacementAttempts++;
            var isConsumptionReplacement = expectedState.Combat is not null
                && replacementState.Combat is not null
                && expectedState.Combat.DamageDispositions.Any(pair =>
                    pair.Value.Status == DamageDispositionStatus.Pending
                    && replacementState.Combat.DamageDispositions.TryGetValue(pair.Key, out var replacement)
                    && replacement.Status == DamageDispositionStatus.Consumed);
            if (isConsumptionReplacement)
            {
                ConsumptionReplacementAttempts++;
                if (failConsumptionReplacement?.Invoke() is true)
                {
                    return false;
                }
            }

            return inner.TryReplace(expectedState, replacementState);
        }

        public bool TryRemove(Guid roomId, out MultiplayerGameState? state) => inner.TryRemove(roomId, out state);

        public bool Exists(Guid roomId) => inner.Exists(roomId);

        public void ArmConsumptionFailure(Func<bool> failureCondition) =>
            failConsumptionReplacement = failureCondition;

        public void ResetConsumptionReplacementAttempts() => ConsumptionReplacementAttempts = 0;
    }

    private sealed record CombatOpponent(
        string Label,
        int Dex,
        int Fighting,
        int Dodge,
        IReadOnlyList<CombatResponse> AvailableResponses,
        int ResponseAllowance,
        CombatResponse NpcResponsePolicy,
        int Str,
        int Siz,
        int CurrentHp,
        int MaxHp,
        int FixedArmor,
        CombatWeaponProfile? Weapon);

    private sealed record InternalCombatResult(
        bool IsSuccess,
        GameErrorCode? ErrorCode,
        bool Changed,
        MultiplayerGameState? State,
        CombatDamageResult? Damage);
}
