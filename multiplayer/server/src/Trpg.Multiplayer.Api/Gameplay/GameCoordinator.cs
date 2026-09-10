using System.Collections.Concurrent;
using Trpg.Multiplayer.Api.Realtime;
using Trpg.Multiplayer.Api.Rooms;

namespace Trpg.Multiplayer.Api.Gameplay;

public sealed class GameCoordinator : IGameCoordinator, IInternalCombatResolutionCoordinator
{
    private const long InitialRevision = 1;
    private const int MaxCombatParticipants = 16;
    private const int MaxCombatHistory = 120;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> gameLocks = new();
    private readonly IRoomStore roomStore;
    private readonly IGameStateStore stateStore;
    private readonly IDiceRoller diceRoller;
    private readonly ICheckResolutionEngine checkEngine;
    private readonly ICombatDamageEngine combatDamageEngine;
    private readonly IHpDamageEngine hpDamageEngine;
    private readonly IHealthStabilizationEngine healthStabilizationEngine;
    private readonly IGameRealtimeNotifier? realtimeNotifier;

    public GameCoordinator(IRoomStore roomStore, IGameStateStore stateStore)
        : this(
            roomStore,
            stateStore,
            new SecureDiceRoller(),
            new CocCheckResolutionEngine(),
            new CocHpDamageEngine(),
            new CocHealthStabilizationEngine())
    {
    }

    public GameCoordinator(
        IRoomStore roomStore,
        IGameStateStore stateStore,
        IDiceRoller diceRoller,
        ICheckResolutionEngine checkEngine,
        IHpDamageEngine hpDamageEngine,
        IGameRealtimeNotifier? realtimeNotifier = null)
        : this(
            roomStore,
            stateStore,
            diceRoller,
            checkEngine,
            hpDamageEngine,
            new CocHealthStabilizationEngine(),
            realtimeNotifier)
    {
    }

    public GameCoordinator(
        IRoomStore roomStore,
        IGameStateStore stateStore,
        IDiceRoller diceRoller,
        ICheckResolutionEngine checkEngine,
        IHpDamageEngine hpDamageEngine,
        IHealthStabilizationEngine healthStabilizationEngine,
        IGameRealtimeNotifier? realtimeNotifier = null)
    {
        this.roomStore = roomStore;
        this.stateStore = stateStore;
        this.diceRoller = diceRoller;
        this.checkEngine = checkEngine;
        combatDamageEngine = new CocCombatDamageEngine();
        this.hpDamageEngine = hpDamageEngine;
        this.healthStabilizationEngine = healthStabilizationEngine;
        this.realtimeNotifier = realtimeNotifier;
    }

    public async Task<GameResult<MultiplayerGameState>> InitializeAsync(InitializeGameCommand command)
    {
        return await WithRoomLockAsync(command.RoomId, () => InitializeCore(command));
    }

    public async Task<GameResult<GameSnapshot>> GetProjectionAsync(Guid roomId, Guid viewerPlayerId)
    {
        return await WithRoomLockAsync(roomId, () =>
        {
            var access = TryGetMember(roomId, viewerPlayerId, out _);
            if (access is not null)
            {
                return GameResult<GameSnapshot>.Failure(access.Value);
            }

            if (!stateStore.TryGet(roomId, out var state) || state is null)
            {
                return GameResult<GameSnapshot>.Failure(GameErrorCode.GameNotFound);
            }

            return GameResult<GameSnapshot>.Success(GameProjection.Build(state, viewerPlayerId), changed: false);
        });
    }

    public async Task<GameResult<CharacterState>> GetCharacterForOwnerAsync(Guid roomId, Guid characterId, Guid playerId)
    {
        return await WithRoomLockAsync(roomId, () =>
        {
            var access = TryGetMember(roomId, playerId, out _);
            if (access is not null)
            {
                return GameResult<CharacterState>.Failure(access.Value);
            }

            if (!stateStore.TryGet(roomId, out var state) || state is null)
            {
                return GameResult<CharacterState>.Failure(GameErrorCode.GameNotFound);
            }

            var character = state.Characters.SingleOrDefault(candidate => candidate.CharacterId == characterId);
            if (character is null)
            {
                return GameResult<CharacterState>.Failure(GameErrorCode.CharacterNotFound);
            }

            return character.OwnerPlayerId == playerId
                ? GameResult<CharacterState>.Success(character, changed: false)
                : GameResult<CharacterState>.Failure(GameErrorCode.CharacterNotOwned);
        });
    }

    public async Task<GameResult<GameCheckResult>> ResolveCheckAsync(ResolveCheckCommand command)
    {
        return await WithRoomLockAsync(command.RoomId, () => ResolveCheckCore(command));
    }

    public async Task<GameResult<HpDamageResult>> ApplyDamageAsync(ApplyDamageCommand command)
    {
        return await WithRoomLockAsync(command.RoomId, async () =>
        {
            var result = ApplyDamageCore(command);
            if (result.IsSuccess && result.Changed && realtimeNotifier is not null)
            {
                await realtimeNotifier.PublishGameSnapshotAsync(command.RoomId);
            }

            return result;
        });
    }

    public async Task<GameResult<HealthStabilizationResult>> ResolveDyingRoundAsync(ResolveDyingRoundCommand command)
    {
        return await WithRoomLockAsync(command.RoomId, async () =>
        {
            var result = ResolveDyingRoundCore(command);
            if (result.IsSuccess && result.Changed && realtimeNotifier is not null)
            {
                await realtimeNotifier.PublishGameSnapshotAsync(command.RoomId);
            }

            return result;
        });
    }

    public async Task<GameResult<HealthStabilizationResult>> ResolveFirstAidAsync(ResolveFirstAidCommand command)
    {
        return await WithRoomLockAsync(command.RoomId, async () =>
        {
            var result = ResolveFirstAidCore(command);
            if (result.IsSuccess && result.Changed && realtimeNotifier is not null)
            {
                await realtimeNotifier.PublishGameSnapshotAsync(command.RoomId);
            }

            return result;
        });
    }

    public async Task<bool> RemoveAsync(Guid roomId)
    {
        return await WithRoomLockAsync(roomId, () => stateStore.TryRemove(roomId, out _));
    }

    internal async Task<GameResult<StartCombatResult>> StartCombatAsync(StartCombatCommand command)
    {
        return await WithRoomLockAsync(command.RoomId, async () =>
        {
            var result = StartCombatCore(command);
            if (result.IsSuccess && result.Changed && realtimeNotifier is not null)
            {
                await realtimeNotifier.PublishGameSnapshotAsync(command.RoomId);
            }

            return result;
        });
    }

    internal async Task<GameResult<BeginOpposedExchangeResult>> BeginOpposedExchangeAsync(BeginOpposedExchangeCommand command)
    {
        return await WithRoomLockAsync(command.RoomId, async () =>
        {
            var result = BeginOpposedExchangeCore(command);
            if (result.IsSuccess && result.Changed && realtimeNotifier is not null)
            {
                await realtimeNotifier.PublishGameSnapshotAsync(command.RoomId);
            }

            return result;
        });
    }

    internal async Task<GameResult<ResolvePendingExchangeResult>> ResolvePendingExchangeAsync(ResolvePendingExchangeCommand command)
    {
        return await WithRoomLockAsync(command.RoomId, async () =>
        {
            var result = ResolvePendingExchangeCore(command);
            if (result.IsSuccess && result.Changed && realtimeNotifier is not null)
            {
                await realtimeNotifier.PublishGameSnapshotAsync(command.RoomId);
            }

            return result;
        });
    }

    internal async Task<GameResult<ResolveCombatDamageResult>> ResolveCombatDamageAsync(ResolveCombatDamageCommand command)
    {
        return await WithRoomLockAsync(command.RoomId, async () =>
        {
            var result = ResolveCombatDamageCore(command);
            // 修改时间：2026-09-07 15:16:09
            // 修改说明：仅在战斗伤害已成功提交且 Changed=true 后，通过既有快照通道发布查看者专属投影。
            // 修改原因：避免陈旧重放、校验失败或 RNG 后提交失败发布未提交或重复状态。
            // 业务影响：不增加公开接口或伤害事件流；连接中玩家收到一次提交后的既有 GameSnapshot。
            if (result.IsSuccess && result.Changed && realtimeNotifier is not null)
            {
                await realtimeNotifier.PublishGameSnapshotAsync(command.RoomId);
            }

            return result;
        });
    }

    internal async Task<GameResult<PassCombatTurnResult>> PassCombatTurnAsync(PassCombatTurnCommand command)
    {
        return await WithRoomLockAsync(command.RoomId, async () =>
        {
            var result = PassCombatTurnCore(command);
            if (result.IsSuccess && result.Changed && realtimeNotifier is not null)
            {
                await realtimeNotifier.PublishGameSnapshotAsync(command.RoomId);
            }

            return result;
        });
    }

    internal async Task<GameResult<EndCombatResult>> EndCombatAsync(EndCombatCommand command)
    {
        return await WithRoomLockAsync(command.RoomId, async () =>
        {
            var result = EndCombatCore(command);
            if (result.IsSuccess && result.Changed && realtimeNotifier is not null)
            {
                await realtimeNotifier.PublishGameSnapshotAsync(command.RoomId);
            }

            return result;
        });
    }

    Task<GameResult<StartCombatResult>> IInternalCombatCoordinator.StartCombatAsync(StartCombatCommand command) =>
        StartCombatAsync(command);

    Task<GameResult<BeginOpposedExchangeResult>> IInternalCombatCoordinator.BeginOpposedExchangeAsync(BeginOpposedExchangeCommand command) =>
        BeginOpposedExchangeAsync(command);

    Task<GameResult<ResolvePendingExchangeResult>> IInternalCombatResolutionCoordinator.ResolvePendingExchangeAsync(ResolvePendingExchangeCommand command) =>
        ResolvePendingExchangeAsync(command);

    Task<GameResult<ResolveCombatDamageResult>> IInternalCombatResolutionCoordinator.ResolveCombatDamageAsync(ResolveCombatDamageCommand command) =>
        ResolveCombatDamageAsync(command);

    Task<GameResult<PassCombatTurnResult>> IInternalCombatResolutionCoordinator.PassCombatTurnAsync(PassCombatTurnCommand command) =>
        PassCombatTurnAsync(command);

    Task<GameResult<EndCombatResult>> IInternalCombatResolutionCoordinator.EndCombatAsync(EndCombatCommand command) =>
        EndCombatAsync(command);

    private GameResult<StartCombatResult> StartCombatCore(StartCombatCommand command)
    {
        var access = TryGetMember(command.RoomId, command.AuthorizedPlayerId, out var room);
        if (access is not null)
        {
            return GameResult<StartCombatResult>.Failure(access.Value);
        }

        if (room!.HostPlayerId != command.AuthorizedPlayerId)
        {
            return GameResult<StartCombatResult>.Failure(GameErrorCode.NotHost);
        }

        if (!stateStore.TryGet(command.RoomId, out var state) || state is null)
        {
            return GameResult<StartCombatResult>.Failure(GameErrorCode.GameNotFound);
        }

        if (state.Revision != command.ExpectedGameRevision)
        {
            return GameResult<StartCombatResult>.Failure(GameErrorCode.StateConflict);
        }

        if (state.Combat?.Active is true)
        {
            return GameResult<StartCombatResult>.Failure(GameErrorCode.InvalidCombat);
        }

        if (command.CharacterIds is null || command.CharacterIds.Count == 0
            || command.Opponents is null || command.Opponents.Count == 0
            || command.CharacterIds.Count + command.Opponents.Count > MaxCombatParticipants)
        {
            return GameResult<StartCombatResult>.Failure(GameErrorCode.InvalidCombat);
        }

        var characterIds = new HashSet<Guid>();
        var participants = new List<(CombatParticipantState Participant, int InputIndex)>();
        for (var index = 0; index < command.CharacterIds.Count; index++)
        {
            var characterId = command.CharacterIds[index];
            if (!characterIds.Add(characterId))
            {
                return GameResult<StartCombatResult>.Failure(GameErrorCode.InvalidParticipant);
            }

            var character = state.Characters.SingleOrDefault(candidate => candidate.CharacterId == characterId);
            if (character is null || character.Health.Dead || !room.Players.Any(player => player.PlayerId == character.OwnerPlayerId)
                || !TryGetCombatStat(character, "dex", out var dex)
                || !TryGetCombatStat(character, "fighting_brawl", out var fighting)
                || !TryGetCombatStat(character, "dodge", out var dodge)
                || !TryGetCombatStat(character, "str", out var str)
                || !TryGetCombatStat(character, "siz", out var siz)
                || character.CombatLoadout is null
                || !TryCreateCombatDamageProfile(
                    str,
                    siz,
                    100,
                    character.CombatLoadout.Weapon,
                    character.CombatLoadout.FixedArmor,
                    out var damageProfile))
            {
                return GameResult<StartCombatResult>.Failure(GameErrorCode.InvalidParticipant);
            }

            participants.Add((CombatSessionState.CreateParticipant(
                new CombatParticipantId($"character:{character.CharacterId}"),
                character.CharacterId,
                character.OwnerPlayerId,
                character.Name,
                "investigator",
                "investigator",
                dex,
                fighting,
                dodge,
                [CombatResponse.Dodge, CombatResponse.FightBack],
                1,
                true,
                damageProfile,
                null,
                null), index));
        }

        for (var index = 0; index < command.Opponents.Count; index++)
        {
            var opponent = command.Opponents[index];
            if (!TryCreateOpponentProfile(opponent, out var damageProfile, out var vitality))
            {
                return GameResult<StartCombatResult>.Failure(GameErrorCode.InvalidCombat);
            }

            participants.Add((CombatSessionState.CreateParticipant(
                new CombatParticipantId($"opponent:{index}"),
                null,
                null,
                opponent.Label.Trim(),
                "opponent",
                "opponent",
                opponent.Dex,
                opponent.Fighting,
                opponent.Dodge,
                opponent.AvailableResponses,
                opponent.ResponseAllowance,
                true,
                damageProfile,
                vitality,
                opponent.NpcResponsePolicy), command.CharacterIds.Count + index));
        }

        var orderedParticipants = participants
            .OrderByDescending(entry => entry.Participant.Dex)
            .ThenBy(entry => entry.InputIndex)
            .Select(entry => entry.Participant)
            .ToArray();
        var dyingSchedule = state.Characters
            .Where(character => characterIds.Contains(character.CharacterId) && character.Health.Dying)
            .ToDictionary(character => character.CharacterId, _ => new DyingScheduleState(1, null));
        var session = new CombatSession(
            Guid.NewGuid(),
            true,
            1,
            0,
            orderedParticipants.Select(participant => participant.ParticipantId).ToArray(),
            orderedParticipants,
            new Dictionary<string, int>(),
            new Dictionary<string, int>(),
            null,
            null,
            [],
            new Dictionary<string, DamageDispositionState>(),
            dyingSchedule,
            DateTimeOffset.UtcNow,
            null,
            null);
        var replacement = new MultiplayerGameState(
            state.RoomId,
            state.Revision + 1,
            state.Status,
            state.CreatedAt,
            state.Characters,
            state.LastCheck,
            session);
        if (!stateStore.TryReplace(state, replacement))
        {
            return GameResult<StartCombatResult>.Failure(GameErrorCode.StateConflict);
        }

        return GameResult<StartCombatResult>.Success(new StartCombatResult(replacement));
    }

    private GameResult<BeginOpposedExchangeResult> BeginOpposedExchangeCore(BeginOpposedExchangeCommand command)
    {
        var access = TryGetMember(command.RoomId, command.RequestingPlayerId, out var room);
        if (access is not null)
        {
            return GameResult<BeginOpposedExchangeResult>.Failure(access.Value);
        }

        var contextError = LoadBeginOpposedExchangeContext(command, out var context);
        if (contextError is not null)
        {
            return GameResult<BeginOpposedExchangeResult>.Failure(contextError.Value);
        }

        var authorityError = ValidatePlayerBeginAuthority(room!, command.RequestingPlayerId, context!.Attacker);
        if (authorityError is not null)
        {
            return GameResult<BeginOpposedExchangeResult>.Failure(authorityError.Value);
        }

        return CommitBeginOpposedExchange(context);
    }

    private GameErrorCode? LoadBeginOpposedExchangeContext(
        BeginOpposedExchangeCommand command,
        out BeginOpposedExchangeContext? context)
    {
        context = null;
        if (!stateStore.TryGet(command.RoomId, out var state) || state?.Combat is not { Active: true } session)
        {
            return GameErrorCode.InvalidCombat;
        }

        if (FindBlockingDamageDisposition(session.DamageDispositions) is not null)
        {
            return GameErrorCode.PendingConflict;
        }

        if (state.Revision != command.ExpectedGameRevision)
        {
            return GameErrorCode.StateConflict;
        }

        if (session.PendingExchange is not null)
        {
            return GameErrorCode.PendingConflict;
        }

        var currentActorId = session.Order.ElementAtOrDefault(session.TurnIndex);
        if (currentActorId is null || currentActorId.Value != command.AttackerParticipantId)
        {
            return GameErrorCode.InvalidParticipant;
        }

        var attacker = session.Participants.SingleOrDefault(participant => participant.ParticipantId.Value == command.AttackerParticipantId);
        var defender = session.Participants.SingleOrDefault(participant => participant.ParticipantId.Value == command.DefenderParticipantId);
        if (attacker is null || defender is null || !attacker.Active || !defender.Active
            || attacker.ParticipantId == defender.ParticipantId || attacker.Side == defender.Side
            || defender.AvailableResponses.Count == 0)
        {
            return GameErrorCode.InvalidParticipant;
        }

        context = new BeginOpposedExchangeContext(state, session, attacker, defender);
        return null;
    }

    private static GameErrorCode? ValidatePlayerBeginAuthority(
        RoomSession room,
        Guid requestingPlayerId,
        CombatParticipantState attacker)
    {
        return (attacker.OwnerPlayerId is Guid owner && owner != requestingPlayerId)
            || (attacker.OwnerPlayerId is null && room.HostPlayerId != requestingPlayerId)
            ? GameErrorCode.InvalidParticipant
            : null;
    }

    private GameResult<BeginOpposedExchangeResult> CommitBeginOpposedExchange(BeginOpposedExchangeContext context)
    {
        var exchangeId = Guid.NewGuid().ToString("N");
        PendingCombatExchange pending;
        try
        {
            pending = CombatSessionState.CreatePendingExchange(
                exchangeId,
                context.Session.Round,
                context.Session.TurnIndex,
                context.Attacker.ParticipantId,
                context.Defender.ParticipantId,
                context.Defender.OwnerPlayerId,
                context.Defender.AvailableResponses,
                context.Session.ResponseCounts.GetValueOrDefault(context.Defender.ParticipantId.Value),
                context.State.Revision,
                DateTimeOffset.UtcNow);
        }
        catch (ArgumentException)
        {
            return GameResult<BeginOpposedExchangeResult>.Failure(GameErrorCode.InvalidResponse);
        }

        var nextSession = context.Session with { PendingExchange = pending };
        var replacement = new MultiplayerGameState(
            context.State.RoomId,
            context.State.Revision + 1,
            context.State.Status,
            context.State.CreatedAt,
            context.State.Characters,
            context.State.LastCheck,
            nextSession);
        if (!stateStore.TryReplace(context.State, replacement))
        {
            return GameResult<BeginOpposedExchangeResult>.Failure(GameErrorCode.StateConflict);
        }

        return GameResult<BeginOpposedExchangeResult>.Success(new BeginOpposedExchangeResult(replacement));
    }

    private GameResult<ResolvePendingExchangeResult> ResolvePendingExchangeCore(ResolvePendingExchangeCommand command)
    {
        if (!stateStore.TryGet(command.RoomId, out var state) || state?.Combat is not { Active: true } session)
        {
            return GameResult<ResolvePendingExchangeResult>.Failure(GameErrorCode.InvalidCombat);
        }

        if (FindBlockingDamageDisposition(session.DamageDispositions) is not null)
        {
            return GameResult<ResolvePendingExchangeResult>.Failure(GameErrorCode.PendingConflict);
        }

        if (state.Revision != command.ExpectedGameRevision)
        {
            return GameResult<ResolvePendingExchangeResult>.Failure(GameErrorCode.StateConflict);
        }

        var pending = session.PendingExchange;
        if (pending is null || pending.ExchangeId != command.ExchangeId)
        {
            return GameResult<ResolvePendingExchangeResult>.Failure(GameErrorCode.InvalidExchange);
        }

        if (!Enum.IsDefined(command.Response) || !pending.AvailableResponses.Contains(command.Response))
        {
            return GameResult<ResolvePendingExchangeResult>.Failure(GameErrorCode.InvalidResponse);
        }

        var attacker = session.Participants.SingleOrDefault(participant => participant.ParticipantId == pending.AttackerParticipantId);
        var defender = session.Participants.SingleOrDefault(participant => participant.ParticipantId == pending.DefenderParticipantId);
        if (attacker is null || defender is null || !attacker.Active || !defender.Active
            || defender.OwnerPlayerId != pending.DefenderOwnerPlayerId)
        {
            return GameResult<ResolvePendingExchangeResult>.Failure(GameErrorCode.InvalidParticipant);
        }

        if (defender.OwnerPlayerId is Guid defenderOwner)
        {
            if (command.RequestingPlayerId != defenderOwner || TryGetMember(command.RoomId, defenderOwner, out _) is not null)
            {
                return GameResult<ResolvePendingExchangeResult>.Failure(GameErrorCode.InvalidParticipant);
            }
        }
        else if (command.RequestingPlayerId is not null)
        {
            return GameResult<ResolvePendingExchangeResult>.Failure(GameErrorCode.InvalidParticipant);
        }

        var outnumberedBonusDice = pending.ResponseCountBefore >= defender.ResponseAllowance ? 1 : 0;
        var attackerCheck = checkEngine.Resolve(new CheckResolutionInput(
            attacker.Fighting,
            "regular",
            outnumberedBonusDice,
            0,
            diceRoller.RollPercentile(outnumberedBonusDice, 0).SelectedRoll));
        var defenderTarget = command.Response == CombatResponse.Dodge ? defender.Dodge : defender.Fighting;
        var defenderCheck = checkEngine.Resolve(new CheckResolutionInput(
            defenderTarget,
            "regular",
            0,
            0,
            diceRoller.RollPercentile(0, 0).SelectedRoll));
        CombatOpposedResolutionResult opposed;
        try
        {
            opposed = new CocCombatOpposedEngine().Resolve(new CombatOpposedResolutionInput(attackerCheck, defenderCheck, command.Response));
        }
        catch (CombatOpposedRuleException)
        {
            return GameResult<ResolvePendingExchangeResult>.Failure(GameErrorCode.InvalidResponse);
        }

        var winner = opposed.WinnerSide == "attacker" ? attacker : opposed.WinnerSide == "defender" ? defender : null;
        var target = opposed.WinnerSide == "attacker" ? defender : opposed.WinnerSide == "defender" ? attacker : null;
        var disposition = winner is null || target is null || opposed.DamageMode is null
            ? null
            : new DamageDispositionState(
                new DamageDispositionData(
                    pending.ExchangeId,
                    winner.ParticipantId,
                    target.ParticipantId,
                    ParseDamageMode(opposed.DamageMode),
                    state.Revision + 1),
                DamageDispositionStatus.Pending,
                null);
        var completed = new CombatExchange(
            pending.ExchangeId, pending.Round, pending.TurnIndex, attacker.ParticipantId, defender.ParticipantId,
            command.Response, attackerCheck, defenderCheck, outnumberedBonusDice, pending.ResponseCountBefore,
            defender.ResponseAllowance, opposed.Outcome, winner?.ParticipantId, disposition, pending.CreatedAt);
        var actionCounts = session.ActionCounts.ToDictionary(pair => pair.Key, pair => pair.Value);
        var responseCounts = session.ResponseCounts.ToDictionary(pair => pair.Key, pair => pair.Value);
        actionCounts[attacker.ParticipantId.Value] = actionCounts.GetValueOrDefault(attacker.ParticipantId.Value) + 1;
        responseCounts[defender.ParticipantId.Value] = responseCounts.GetValueOrDefault(defender.ParticipantId.Value) + 1;
        var damageDispositions = session.DamageDispositions.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (disposition is not null)
        {
            damageDispositions.Add(disposition.Disposition.ExchangeId, disposition);
        }

        var completedSession = session with
        {
            PendingExchange = null,
            LastExchange = completed,
            History = session.History.Append(completed).TakeLast(MaxCombatHistory).ToArray(),
            DamageDispositions = damageDispositions
        };
        var advance = AdvanceTurn(state, completedSession, actionCounts, responseCounts);
        return ReplaceCombatState(state, advance.Session, replacement => new ResolvePendingExchangeResult(replacement), advance.Characters);
    }

    private GameResult<ResolveCombatDamageResult> ResolveCombatDamageCore(ResolveCombatDamageCommand command)
    {
        if (!roomStore.TryGet(command.RoomId, out var room) || room is null)
        {
            return GameResult<ResolveCombatDamageResult>.Failure(GameErrorCode.RoomNotFound);
        }

        if (room.Status == RoomStatus.Closed)
        {
            return GameResult<ResolveCombatDamageResult>.Failure(GameErrorCode.RoomClosed);
        }

        if (!stateStore.TryGet(command.RoomId, out var state) || state?.Combat is not { Active: true } session)
        {
            return GameResult<ResolveCombatDamageResult>.Failure(GameErrorCode.InvalidCombat);
        }

        if (session.DamageDispositions is null)
        {
            throw new CombatDamageStateInvariantException("The canonical combat damage registry is missing.");
        }

        if (string.IsNullOrWhiteSpace(command.ExchangeId)
            || !session.DamageDispositions.TryGetValue(command.ExchangeId, out var dispositionState)
            || dispositionState is null)
        {
            return GameResult<ResolveCombatDamageResult>.Failure(GameErrorCode.InvalidExchange);
        }

        ValidateDamageDispositionState(command.ExchangeId, dispositionState);
        if (dispositionState.Status == DamageDispositionStatus.Consumed)
        {
            return GameResult<ResolveCombatDamageResult>.Success(
                new ResolveCombatDamageResult(state, dispositionState.Result!),
                changed: false);
        }

        if (state.Revision != command.ExpectedGameRevision)
        {
            return GameResult<ResolveCombatDamageResult>.Failure(GameErrorCode.StateConflict);
        }

        var blocker = FindValidatedBlockingDamageDisposition(session.DamageDispositions);
        if (blocker is null || !string.Equals(
                blocker.Disposition.ExchangeId,
                command.ExchangeId,
                StringComparison.Ordinal))
        {
            return GameResult<ResolveCombatDamageResult>.Failure(GameErrorCode.PendingConflict);
        }

        var disposition = dispositionState.Disposition;
        var owner = FindUniqueParticipant(session, disposition.OwnerParticipantId);
        var target = FindUniqueParticipant(session, disposition.TargetParticipantId);
        if (owner is null || target is null || owner.ParticipantId == target.ParticipantId || owner.Side == target.Side)
        {
            return GameResult<ResolveCombatDamageResult>.Failure(GameErrorCode.InvalidParticipant);
        }

        ValidateCanonicalParticipantDamageState(state, owner);
        var targetHp = ValidateCanonicalParticipantDamageState(state, target);
        if (!Enum.IsDefined(disposition.Mode))
        {
            throw new CombatDamageStateInvariantException(
                $"Exchange '{command.ExchangeId}' contains an unsupported canonical damage mode.");
        }

        var rollPlan = CreateCombatDamageRollPlan(owner.DamageProfile, disposition.Mode);
        var targetAlreadyIneligible = !target.Active || IsTargetAlreadyIneligible(state, target);
        if (targetAlreadyIneligible)
        {
            var ineligibleResult = new CombatDamageResult(
                disposition.ExchangeId,
                disposition.OwnerParticipantId,
                disposition.TargetParticipantId,
                disposition.Mode,
                CombatDamageOutcome.TargetAlreadyIneligible,
                null,
                null,
                null,
                null,
                0,
                target.DamageProfile.FixedArmor,
                0,
                targetHp,
                targetHp,
                false,
                true,
                DateTimeOffset.UtcNow);
            return ConsumeCombatDamageDisposition(state, session, dispositionState, ineligibleResult, rngBegan: false);
        }

        var rngBegan = false;
        GenericDiceRoll? weaponRoll = null;
        GenericDiceRoll? damageBonusRoll = null;
        if (rollPlan.WeaponRoll is not null)
        {
            rngBegan = true;
            weaponRoll = diceRoller.RollDice(rollPlan.WeaponRoll);
        }

        if (rollPlan.DamageBonusRoll is not null)
        {
            rngBegan = true;
            damageBonusRoll = diceRoller.RollDice(rollPlan.DamageBonusRoll);
        }

        var calculation = combatDamageEngine.Resolve(new CombatDamageInput(
            owner.DamageProfile.Weapon,
            owner.DamageProfile.DamageBonus,
            disposition.Mode,
            weaponRoll,
            damageBonusRoll,
            target.DamageProfile.FixedArmor));

        var nextCharacters = state.Characters;
        var nextParticipants = session.Participants;
        var nextDyingSchedule = session.DyingSchedule;
        var hpAfter = targetHp;
        var hpDamageApplied = false;
        var targetDefeated = false;

        // 修改时间：2026-09-07 14:56:39
        // 修改说明：在房间锁保护的一次状态替换中应用调查员 HP 或战斗域对手生命值。
        // 修改原因：Task 7 已保留待消费伤害，但尚未原子提交生命值、参与状态与伤害结果。
        // 业务影响：调查员正数净伤害走唯一 HP 引擎；对手只改变战斗域生命值，零伤害不触发 HP/CON。
        if (target.Kind == "investigator")
        {
            var characterId = target.CharacterId!.Value;
            var character = state.Characters.Single(candidate => candidate.CharacterId == characterId);
            if (calculation.NetDamage > 0)
            {
                int? conRoll = null;
                if (CocHpDamageEngine.RequiresConRoll(character.Health, calculation.NetDamage))
                {
                    rngBegan = true;
                    conRoll = diceRoller.RollPercentile(0, 0).SelectedRoll;
                }

                var hpResolution = hpDamageEngine.Apply(
                    character.Health,
                    new HpDamageInput($"combat:{disposition.ExchangeId}", calculation.NetDamage, conRoll));
                hpAfter = hpResolution.State.CurrentHp;
                hpDamageApplied = hpResolution.Changed;
                targetDefeated = hpResolution.State.Dead;
                nextCharacters = state.Characters
                    .Select(candidate => candidate.CharacterId == characterId
                        ? candidate.WithHealth(hpResolution.State)
                        : candidate)
                    .ToArray();

                var schedule = session.DyingSchedule.ToDictionary(pair => pair.Key, pair => pair.Value);
                if (targetDefeated)
                {
                    nextParticipants = session.Participants
                        .Select(participant => participant.ParticipantId == target.ParticipantId
                            ? participant with { Active = false }
                            : participant)
                        .ToArray();
                    schedule.Remove(characterId);
                }
                else if (!character.Health.Dying && hpResolution.State.Dying)
                {
                    schedule[characterId] = new DyingScheduleState(session.Round, null);
                }

                nextDyingSchedule = schedule;
            }
        }
        else if (calculation.NetDamage > 0)
        {
            hpAfter = Math.Max(0, targetHp - calculation.NetDamage);
            targetDefeated = hpAfter == 0;
            nextParticipants = session.Participants
                .Select(participant => participant.ParticipantId == target.ParticipantId
                    ? participant with
                    {
                        Active = !targetDefeated,
                        OpponentVitality = participant.OpponentVitality! with { CurrentHp = hpAfter }
                    }
                    : participant)
                .ToArray();
        }

        var result = new CombatDamageResult(
            disposition.ExchangeId,
            disposition.OwnerParticipantId,
            disposition.TargetParticipantId,
            disposition.Mode,
            CombatDamageOutcome.Applied,
            owner.DamageProfile.Weapon.WeaponId,
            owner.DamageProfile.Weapon.Damage.Text,
            calculation.WeaponResult,
            calculation.DamageBonusResult,
            calculation.GrossDamage,
            calculation.Armor,
            calculation.NetDamage,
            targetHp,
            hpAfter,
            hpDamageApplied,
            targetDefeated,
            DateTimeOffset.UtcNow);
        var appliedSession = session with
        {
            Participants = nextParticipants,
            DyingSchedule = nextDyingSchedule
        };
        var repaired = RepairCombatAfterDamage(state, appliedSession, nextCharacters);
        rngBegan |= repaired.RngBegan;
        return ConsumeCombatDamageDisposition(
            state,
            repaired.Session,
            dispositionState,
            result,
            rngBegan,
            repaired.Characters);
    }

    private GameResult<ResolveCombatDamageResult> ConsumeCombatDamageDisposition(
        MultiplayerGameState state,
        CombatSession session,
        DamageDispositionState pending,
        CombatDamageResult result,
        bool rngBegan,
        IReadOnlyList<CharacterState>? characters = null)
    {
        var damageDispositions = session.DamageDispositions.ToDictionary(pair => pair.Key, pair => pair.Value);
        damageDispositions[pending.Disposition.ExchangeId] = pending with
        {
            Status = DamageDispositionStatus.Consumed,
            Result = result
        };
        var nextSession = session with { DamageDispositions = damageDispositions };
        var replacement = new MultiplayerGameState(
            state.RoomId,
            state.Revision + 1,
            state.Status,
            state.CreatedAt,
            characters ?? state.Characters,
            state.LastCheck,
            nextSession);

        if (!stateStore.TryReplace(state, replacement))
        {
            if (rngBegan)
            {
                // 修改时间：2026-09-07 14:40:33
                // 修改说明：将伤害掷骰后的存储替换失败升级为不可重试的内部一致性异常。
                // 修改原因：再次执行同一 ExchangeId 会重新掷骰并破坏 exactly-once 语义。
                // 业务影响：禁止把 RNG 后的 CAS 失败降级为可重试 StateConflict，也不会触发通知。
                throw new CombatDamageCommitInvariantException(
                    $"Combat damage exchange '{pending.Disposition.ExchangeId}' failed to commit after RNG and must not be re-rolled.");
            }

            return GameResult<ResolveCombatDamageResult>.Failure(GameErrorCode.StateConflict);
        }

        return GameResult<ResolveCombatDamageResult>.Success(new ResolveCombatDamageResult(replacement, result));
    }

    private CombatDamageRepair RepairCombatAfterDamage(
        MultiplayerGameState state,
        CombatSession session,
        IReadOnlyList<CharacterState> characters)
    {
        var ended = EndCombatIfSideDefeated(session);
        if (!ended.Active)
        {
            return new CombatDamageRepair(characters, ended, false);
        }

        var currentIndex = Math.Clamp(session.TurnIndex, 0, session.Order.Count);
        var nextTurn = FindNextActiveTurn(session, currentIndex);
        if (nextTurn < session.Order.Count)
        {
            return new CombatDamageRepair(characters, session with { TurnIndex = nextTurn }, false);
        }

        var stateAfterDamage = new MultiplayerGameState(
            state.RoomId,
            state.Revision,
            state.Status,
            state.CreatedAt,
            characters,
            state.LastCheck,
            session);
        var dyingRngBegan = WillRollDyingCheckAtRoundWrap(stateAfterDamage, session);
        var wrapped = AdvanceTurn(stateAfterDamage, session, session.ActionCounts, session.ResponseCounts);
        return new CombatDamageRepair(
            wrapped.Characters,
            EndCombatIfSideDefeated(wrapped.Session),
            dyingRngBegan);
    }

    private static bool WillRollDyingCheckAtRoundWrap(
        MultiplayerGameState state,
        CombatSession session)
    {
        var characters = state.Characters.ToDictionary(character => character.CharacterId);
        var participants = session.Participants.ToDictionary(participant => participant.ParticipantId);
        foreach (var participantId in session.Order)
        {
            if (!participants.TryGetValue(participantId, out var participant)
                || !participant.Active
                || participant.CharacterId is not Guid characterId
                || !characters.TryGetValue(characterId, out var character)
                || !CanScheduleDying(character.Health)
                || !session.DyingSchedule.TryGetValue(characterId, out var entry))
            {
                continue;
            }

            if (session.Round > entry.ObservedRound && entry.LastCheckCompletedRound != session.Round)
            {
                return true;
            }
        }

        return false;
    }

    private static CombatSession EndCombatIfSideDefeated(CombatSession session)
    {
        string? reason = null;
        if (!session.Participants.Any(participant => participant.Side == "opponent" && participant.Active))
        {
            reason = "opposition_defeated";
        }
        else if (!session.Participants.Any(participant => participant.Side == "investigator" && participant.Active))
        {
            reason = "investigators_defeated";
        }

        return reason is null
            ? session
            : session with
            {
                Active = false,
                PendingExchange = null,
                EndedAt = DateTimeOffset.UtcNow,
                EndReason = reason
            };
    }

    private GameResult<PassCombatTurnResult> PassCombatTurnCore(PassCombatTurnCommand command)
    {
        var access = TryGetMember(command.RoomId, command.RequestingPlayerId, out var room);
        if (access is not null)
        {
            return GameResult<PassCombatTurnResult>.Failure(access.Value);
        }

        var context = LoadPassCombatTurnContext(command.RoomId, command.ExpectedGameRevision, out var errorCode);
        if (context is null)
        {
            return GameResult<PassCombatTurnResult>.Failure(errorCode!.Value);
        }

        return !ValidatePlayerPassAuthority(context, command.RequestingPlayerId, room!)
            ? GameResult<PassCombatTurnResult>.Failure(GameErrorCode.InvalidParticipant)
            : CommitPassCombatTurn(context);
    }

    private PassCombatTurnContext? LoadPassCombatTurnContext(
        Guid roomId,
        long expectedGameRevision,
        out GameErrorCode? errorCode)
    {
        errorCode = null;
        if (!stateStore.TryGet(roomId, out var state) || state?.Combat is not { Active: true } session)
        {
            errorCode = GameErrorCode.InvalidCombat;
            return null;
        }

        if (FindBlockingDamageDisposition(session.DamageDispositions) is not null)
        {
            errorCode = GameErrorCode.PendingConflict;
            return null;
        }

        if (state.Revision != expectedGameRevision)
        {
            errorCode = GameErrorCode.StateConflict;
            return null;
        }

        if (session.PendingExchange is not null)
        {
            errorCode = GameErrorCode.PendingConflict;
            return null;
        }

        var actor = session.Participants.SingleOrDefault(
            participant => participant.ParticipantId == session.Order.ElementAtOrDefault(session.TurnIndex));
        if (actor is null || !actor.Active)
        {
            errorCode = GameErrorCode.InvalidParticipant;
            return null;
        }

        return new PassCombatTurnContext(state, session, actor);
    }

    private static bool ValidatePlayerPassAuthority(
        PassCombatTurnContext context,
        Guid requestingPlayerId,
        RoomSession room) =>
        CanControlActor(context.Actor, requestingPlayerId, room);

    private GameResult<PassCombatTurnResult> CommitPassCombatTurn(PassCombatTurnContext context)
    {
        var actionCounts = context.Session.ActionCounts.ToDictionary(pair => pair.Key, pair => pair.Value);
        actionCounts[context.Actor.ParticipantId.Value] =
            actionCounts.GetValueOrDefault(context.Actor.ParticipantId.Value) + 1;
        var advance = AdvanceTurn(context.State, context.Session, actionCounts, context.Session.ResponseCounts);
        return ReplaceCombatState(
            context.State,
            advance.Session,
            replacement => new PassCombatTurnResult(replacement),
            advance.Characters);
    }

    private GameResult<EndCombatResult> EndCombatCore(EndCombatCommand command)
    {
        var access = TryGetMember(command.RoomId, command.AuthorizedPlayerId, out var room);
        if (access is not null)
        {
            return GameResult<EndCombatResult>.Failure(access.Value);
        }

        if (room!.HostPlayerId != command.AuthorizedPlayerId)
        {
            return GameResult<EndCombatResult>.Failure(GameErrorCode.NotHost);
        }

        if (!stateStore.TryGet(command.RoomId, out var state) || state?.Combat is not { Active: true } session)
        {
            return GameResult<EndCombatResult>.Failure(GameErrorCode.InvalidCombat);
        }

        if (FindBlockingDamageDisposition(session.DamageDispositions) is not null)
        {
            return GameResult<EndCombatResult>.Failure(GameErrorCode.PendingConflict);
        }

        if (state.Revision != command.ExpectedGameRevision)
        {
            return GameResult<EndCombatResult>.Failure(GameErrorCode.StateConflict);
        }

        var nextSession = session with
        {
            Active = false,
            PendingExchange = null,
            EndedAt = DateTimeOffset.UtcNow,
            EndReason = "combat_ended_before_resolution"
        };
        return ReplaceCombatState(state, nextSession, replacement => new EndCombatResult(replacement));
    }

    private GameResult<T> ReplaceCombatState<T>(
        MultiplayerGameState state,
        CombatSession session,
        Func<MultiplayerGameState, T> resultFactory,
        IReadOnlyList<CharacterState>? characters = null)
    {
        var replacement = new MultiplayerGameState(
            state.RoomId, state.Revision + 1, state.Status, state.CreatedAt, characters ?? state.Characters, state.LastCheck, session);
        return stateStore.TryReplace(state, replacement)
            ? GameResult<T>.Success(resultFactory(replacement))
            : GameResult<T>.Failure(GameErrorCode.StateConflict);
    }

    private static bool CanControlActor(CombatParticipantState actor, Guid playerId, RoomSession room) =>
        actor.OwnerPlayerId is Guid owner ? owner == playerId : room.HostPlayerId == playerId;

    private static DamageDispositionState? FindBlockingDamageDisposition(
        IReadOnlyDictionary<string, DamageDispositionState> damageDispositions) =>
        damageDispositions.Values
            .Where(entry => entry.Status == DamageDispositionStatus.Pending)
            .OrderBy(entry => entry.Disposition.CreatedGameRevision)
            .ThenBy(entry => entry.Disposition.ExchangeId, StringComparer.Ordinal)
            .FirstOrDefault();

    private static DamageDispositionState? FindValidatedBlockingDamageDisposition(
        IReadOnlyDictionary<string, DamageDispositionState> damageDispositions)
    {
        foreach (var entry in damageDispositions)
        {
            if (entry.Value is null)
            {
                throw new CombatDamageStateInvariantException(
                    $"Combat damage registry entry '{entry.Key}' has no canonical state.");
            }

            ValidateDamageDispositionState(entry.Key, entry.Value);
        }

        return FindBlockingDamageDisposition(damageDispositions);
    }

    private static void ValidateDamageDispositionState(string registryKey, DamageDispositionState state)
    {
        if (state.Disposition is null
            || string.IsNullOrWhiteSpace(state.Disposition.ExchangeId)
            || !string.Equals(registryKey, state.Disposition.ExchangeId, StringComparison.Ordinal)
            || !Enum.IsDefined(state.Status)
            || (state.Status == DamageDispositionStatus.Pending && state.Result is not null)
            || (state.Status == DamageDispositionStatus.Consumed && state.Result is null))
        {
            throw new CombatDamageStateInvariantException(
                $"Combat damage registry entry '{registryKey}' has an inconsistent status or result.");
        }

        if (state.Status != DamageDispositionStatus.Consumed)
        {
            return;
        }

        var result = state.Result!;
        if (!string.Equals(result.ExchangeId, state.Disposition.ExchangeId, StringComparison.Ordinal)
            || result.OwnerParticipantId != state.Disposition.OwnerParticipantId
            || result.TargetParticipantId != state.Disposition.TargetParticipantId
            || result.DamageMode != state.Disposition.Mode
            || !Enum.IsDefined(result.Outcome))
        {
            throw new CombatDamageStateInvariantException(
                $"Consumed combat damage registry entry '{registryKey}' has an inconsistent immutable result.");
        }
    }

    private static CombatParticipantState? FindUniqueParticipant(
        CombatSession session,
        CombatParticipantId participantId)
    {
        if (participantId is null || string.IsNullOrWhiteSpace(participantId.Value))
        {
            return null;
        }

        var matches = session.Participants
            .Where(participant => participant.ParticipantId == participantId)
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static int ValidateCanonicalParticipantDamageState(
        MultiplayerGameState state,
        CombatParticipantState participant)
    {
        var maximumStat = participant.Kind switch
        {
            "investigator" when participant.Side == "investigator" => 100,
            "opponent" when participant.Side == "opponent" => 999,
            _ => 0
        };
        if (maximumStat == 0
            || participant.DamageProfile is null
            || !TryCreateCombatDamageProfile(
                participant.DamageProfile.Str,
                participant.DamageProfile.Siz,
                maximumStat,
                participant.DamageProfile.Weapon,
                participant.DamageProfile.FixedArmor,
                out var normalizedProfile)
            || normalizedProfile != participant.DamageProfile)
        {
            throw new CombatDamageStateInvariantException(
                $"Participant '{participant.ParticipantId.Value}' has an invalid canonical combat damage profile.");
        }

        if (participant.Kind == "investigator")
        {
            var character = participant.CharacterId is Guid characterId
                ? state.Characters.SingleOrDefault(candidate => candidate.CharacterId == characterId)
                : null;
            if (character is null || participant.OwnerPlayerId is null || participant.OpponentVitality is not null)
            {
                throw new CombatDamageStateInvariantException(
                    $"Investigator participant '{participant.ParticipantId.Value}' has inconsistent canonical identity or vitality.");
            }

            return character.Health.CurrentHp;
        }

        if (participant.CharacterId is not null
            || participant.OwnerPlayerId is not null
            || participant.OpponentVitality is not { CurrentHp: >= 0 } vitality
            || vitality.MaxHp <= 0
            || vitality.CurrentHp > vitality.MaxHp)
        {
            throw new CombatDamageStateInvariantException(
                $"Opponent participant '{participant.ParticipantId.Value}' has invalid canonical vitality.");
        }

        return vitality.CurrentHp;
    }

    private static bool IsTargetAlreadyIneligible(
        MultiplayerGameState state,
        CombatParticipantState target)
    {
        if (target.Kind == "investigator")
        {
            var character = state.Characters.Single(candidate => candidate.CharacterId == target.CharacterId);
            return character.Health.Dead;
        }

        return target.OpponentVitality!.CurrentHp <= 0;
    }

    private static CombatDamageRollPlan CreateCombatDamageRollPlan(
        CombatDamageProfile ownerProfile,
        CombatDamageMode mode)
    {
        if (mode == CombatDamageMode.InitiatorExtremeEligible)
        {
            return new CombatDamageRollPlan(null, null);
        }

        if (mode is not (CombatDamageMode.Regular or CombatDamageMode.FightBackRegularCap))
        {
            throw new CombatDamageStateInvariantException("The canonical combat damage mode has no supported roll plan.");
        }

        var weaponRoll = new DiceRollRequest(
            ownerProfile.Weapon.Damage.Count,
            ownerProfile.Weapon.Damage.Faces);
        var damageBonusRoll = ownerProfile.Weapon.AddsDamageBonus
            && ownerProfile.DamageBonus.Kind == DamageBonusKind.Dice
            ? new DiceRollRequest(ownerProfile.DamageBonus.Count, ownerProfile.DamageBonus.Faces)
            : null;
        return new CombatDamageRollPlan(weaponRoll, damageBonusRoll);
    }

    private sealed record CombatDamageRollPlan(
        DiceRollRequest? WeaponRoll,
        DiceRollRequest? DamageBonusRoll);

    private static CombatDamageMode ParseDamageMode(string mode) => mode switch
    {
        "regular" => CombatDamageMode.Regular,
        "initiator_extreme_eligible" => CombatDamageMode.InitiatorExtremeEligible,
        "fight_back_regular_cap" => CombatDamageMode.FightBackRegularCap,
        _ => throw new InvalidOperationException($"Unsupported canonical combat damage mode '{mode}'.")
    };

    private CombatTurnAdvance AdvanceTurn(
        MultiplayerGameState state,
        CombatSession session,
        IReadOnlyDictionary<string, int> actionCounts,
        IReadOnlyDictionary<string, int> responseCounts)
    {
        var nextTurn = FindNextActiveTurn(session, session.TurnIndex + 1);
        if (nextTurn < session.Order.Count)
        {
            return new CombatTurnAdvance(
                state.Characters,
                session with { TurnIndex = nextTurn, ActionCounts = actionCounts, ResponseCounts = responseCounts });
        }

        var finishedRound = session.Round;
        var characters = state.Characters.ToDictionary(character => character.CharacterId);
        var participants = session.Participants.ToDictionary(participant => participant.ParticipantId);
        var schedule = session.DyingSchedule.ToDictionary(pair => pair.Key, pair => pair.Value);

        foreach (var participantId in session.Order)
        {
            if (!participants.TryGetValue(participantId, out var participant) || participant.CharacterId is not Guid characterId
                || !characters.TryGetValue(characterId, out var character))
            {
                continue;
            }

            if (!participant.Active || !CanScheduleDying(character.Health))
            {
                schedule.Remove(characterId);
                continue;
            }

            if (!schedule.TryGetValue(characterId, out var entry))
            {
                schedule[characterId] = new DyingScheduleState(finishedRound, null);
                continue;
            }

            if (finishedRound <= entry.ObservedRound || entry.LastCheckCompletedRound == finishedRound)
            {
                continue;
            }

            var resolution = ResolveHealth(() => healthStabilizationEngine.ResolveDyingRound(
                character.Health,
                new DyingRoundInput(
                    diceRoller.RollPercentile(0, 0).SelectedRoll,
                    $"combat-round-{finishedRound}-{characterId:N}",
                    DateTimeOffset.UtcNow)));
            if (resolution.Error is not null || resolution.Value is null)
            {
                continue;
            }

            characters[characterId] = character.WithHealth(resolution.Value.State);
            if (resolution.Value.State.Dead)
            {
                participants[participantId] = participant with { Active = false };
                schedule.Remove(characterId);
            }
            else
            {
                schedule[characterId] = new DyingScheduleState(entry.ObservedRound, finishedRound);
            }
        }

        var nextParticipants = session.Participants.Select(participant => participants[participant.ParticipantId]).ToArray();
        var wrappedSession = session with
        {
            Round = finishedRound + 1,
            TurnIndex = 0,
            ActionCounts = new Dictionary<string, int>(),
            ResponseCounts = new Dictionary<string, int>(),
            Participants = nextParticipants,
            DyingSchedule = schedule
        };
        wrappedSession = wrappedSession with { TurnIndex = FindNextActiveTurn(wrappedSession, 0) };

        return new CombatTurnAdvance(
            state.Characters.Select(character => characters[character.CharacterId]).ToArray(),
            wrappedSession);
    }

    private static bool CanScheduleDying(CharacterHealthState health) =>
        health.Dying && health.Stabilized is null && !health.Dead;

    private static int FindNextActiveTurn(CombatSession session, int startIndex)
    {
        for (var index = startIndex; index < session.Order.Count; index++)
        {
            var participant = session.Participants.SingleOrDefault(candidate => candidate.ParticipantId == session.Order[index]);
            if (participant is { Active: true })
            {
                return index;
            }
        }

        return session.Order.Count;
    }

    private sealed record CombatTurnAdvance(IReadOnlyList<CharacterState> Characters, CombatSession Session);

    private sealed record PassCombatTurnContext(
        MultiplayerGameState State,
        CombatSession Session,
        CombatParticipantState Actor);

    private sealed record CombatDamageRepair(
        IReadOnlyList<CharacterState> Characters,
        CombatSession Session,
        bool RngBegan);

    private static bool TryGetCombatStat(CharacterState character, string key, out int value) =>
        character.CheckValues.TryGetValue(key, out value) && value is >= 1 and <= 100;

    private static bool TryCreateOpponentProfile(
        OpponentDefinition opponent,
        out CombatDamageProfile damageProfile,
        out OpponentVitalityState vitality)
    {
        damageProfile = default!;
        vitality = default!;
        if (opponent is null
            || string.IsNullOrWhiteSpace(opponent.Label)
            || opponent.Dex is < 1 or > 100
            || opponent.Fighting is < 1 or > 100
            || opponent.Dodge is < 1 or > 100
            || opponent.AvailableResponses is not { Count: > 0 }
            || opponent.AvailableResponses.Any(response => !Enum.IsDefined(response))
            || opponent.ResponseAllowance < 0
            || !Enum.IsDefined(opponent.NpcResponsePolicy)
            || !opponent.AvailableResponses.Contains(opponent.NpcResponsePolicy)
            || opponent.CurrentHp <= 0
            || opponent.MaxHp < opponent.CurrentHp
            || !TryCreateCombatDamageProfile(
                opponent.Str,
                opponent.Siz,
                999,
                opponent.Weapon,
                opponent.FixedArmor,
                out damageProfile))
        {
            return false;
        }

        vitality = new OpponentVitalityState(opponent.CurrentHp, opponent.MaxHp);
        return true;
    }

    private static bool TryCreateCombatDamageProfile(
        int str,
        int siz,
        int maximumStat,
        CombatWeaponProfile? weapon,
        int fixedArmor,
        out CombatDamageProfile damageProfile)
    {
        damageProfile = default!;
        if (str < 1 || str > maximumStat
            || siz < 1 || siz > maximumStat
            || fixedArmor is < 0 or > 99
            || weapon?.Damage is null
            || string.IsNullOrWhiteSpace(weapon.WeaponId)
            || string.IsNullOrWhiteSpace(weapon.Label))
        {
            return false;
        }

        try
        {
            var normalizedWeapon = CocCombatDamageRules.NormalizeWeapon(
                weapon.WeaponId.Trim(),
                weapon.Label.Trim(),
                weapon.Damage.Text,
                weapon.AddsDamageBonus,
                weapon.Mode);
            damageProfile = new CombatDamageProfile(
                str,
                siz,
                CocCombatDamageRules.DeriveDamageBonus(str, siz),
                normalizedWeapon,
                fixedArmor);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private GameResult<MultiplayerGameState> InitializeCore(InitializeGameCommand command)
    {
        var access = TryGetMember(command.RoomId, command.HostPlayerId, out var room);
        if (access is not null)
        {
            return GameResult<MultiplayerGameState>.Failure(access.Value);
        }

        if (room!.HostPlayerId != command.HostPlayerId
            || room.Players.Single(player => player.PlayerId == command.HostPlayerId).IsHost is false)
        {
            return GameResult<MultiplayerGameState>.Failure(GameErrorCode.NotHost);
        }

        if (stateStore.Exists(command.RoomId))
        {
            return GameResult<MultiplayerGameState>.Failure(GameErrorCode.AlreadyInitialized);
        }

        if (command.Characters is null || command.Characters.Count == 0)
        {
            return GameResult<MultiplayerGameState>.Failure(GameErrorCode.InvalidRoster);
        }

        var roomPlayerIds = room.Players.Select(player => player.PlayerId).ToHashSet();
        var ownerIds = new HashSet<Guid>();
        var characters = new List<CharacterState>(command.Characters.Count);
        foreach (var requested in command.Characters)
        {
            if (!roomPlayerIds.Contains(requested.PlayerId))
            {
                return GameResult<MultiplayerGameState>.Failure(GameErrorCode.UnknownPlayer);
            }

            if (!ownerIds.Add(requested.PlayerId))
            {
                return GameResult<MultiplayerGameState>.Failure(GameErrorCode.DuplicateCharacterOwnership);
            }

            if (string.IsNullOrWhiteSpace(requested.Name)
                || requested.Name.Length > 100
                || requested.CheckValues is null
                || requested.CheckValues.Count == 0
                || requested.CheckValues.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value is < 1 or > 100)
                || requested.CheckValues.Keys
                    .GroupBy(key => key, StringComparer.OrdinalIgnoreCase)
                    .Any(group => group.Count() > 1))
            {
                return GameResult<MultiplayerGameState>.Failure(GameErrorCode.InvalidRoster);
            }

            if (requested.Health is null
                || requested.Health.MaxHp < 1
                || requested.Health.CurrentHp < 0
                || requested.Health.CurrentHp > requested.Health.MaxHp
                || requested.Health.Con is < 0 or > 100)
            {
                return GameResult<MultiplayerGameState>.Failure(GameErrorCode.InvalidHealthSetup);
            }

            characters.Add(new CharacterState(
                Guid.NewGuid(),
                requested.PlayerId,
                requested.Name.Trim(),
                requested.CheckValues,
                new CharacterHealthState(
                    requested.Health.CurrentHp,
                    requested.Health.MaxHp,
                    requested.Health.Con,
                    MajorWound: false,
                    Unconscious: requested.Health.CurrentHp == 0,
                    Dying: false,
                    Dead: false,
                    History: [],
                    LastDamageEvent: null)));
        }

        var state = new MultiplayerGameState(
            command.RoomId,
            InitialRevision,
            MultiplayerGameStatus.Active,
            DateTimeOffset.UtcNow,
            characters);

        return stateStore.TryAdd(state)
            ? GameResult<MultiplayerGameState>.Success(state)
            : GameResult<MultiplayerGameState>.Failure(GameErrorCode.AlreadyInitialized);
    }

    private GameResult<GameCheckResult> ResolveCheckCore(ResolveCheckCommand command)
    {
        var access = TryGetMember(command.RoomId, command.PlayerId, out _);
        if (access is not null)
        {
            return GameResult<GameCheckResult>.Failure(access.Value);
        }

        if (!stateStore.TryGet(command.RoomId, out var state) || state is null)
        {
            return GameResult<GameCheckResult>.Failure(GameErrorCode.GameNotFound);
        }

        var character = state.Characters.SingleOrDefault(candidate => candidate.CharacterId == command.CharacterId);
        if (character is null)
        {
            return GameResult<GameCheckResult>.Failure(GameErrorCode.CharacterNotFound);
        }

        if (character.OwnerPlayerId != command.PlayerId)
        {
            return GameResult<GameCheckResult>.Failure(GameErrorCode.CharacterNotOwned);
        }

        if (string.IsNullOrWhiteSpace(command.CheckKey)
            || !character.CheckValues.TryGetValue(command.CheckKey, out var target))
        {
            return GameResult<GameCheckResult>.Failure(GameErrorCode.InvalidCheckKey);
        }

        if (!CheckDifficulty.IsSupported(command.Difficulty)
            || command.BonusDice is < 0 or > 2
            || command.PenaltyDice is < 0 or > 2)
        {
            return GameResult<GameCheckResult>.Failure(GameErrorCode.InvalidCheckRequest);
        }

        var dice = diceRoller.RollPercentile(command.BonusDice, command.PenaltyDice);
        var resolution = checkEngine.Resolve(new CheckResolutionInput(
            target,
            command.Difficulty,
            command.BonusDice,
            command.PenaltyDice,
            dice.SelectedRoll));
        var nextRevision = state.Revision + 1;
        var record = new GameCheckRecord(
            Guid.NewGuid(),
            command.PlayerId,
            command.CharacterId,
            command.CheckKey,
            resolution.Target,
            resolution.Roll,
            resolution.SuccessLevel,
            resolution.Passed,
            nextRevision,
            DateTimeOffset.UtcNow);
        var replacement = new MultiplayerGameState(
            state.RoomId,
            nextRevision,
            state.Status,
            state.CreatedAt,
            state.Characters,
            record,
            state.Combat);
        if (!stateStore.TryReplace(state, replacement))
        {
            return GameResult<GameCheckResult>.Failure(GameErrorCode.StateConflict);
        }

        return GameResult<GameCheckResult>.Success(new GameCheckResult(
            GameProjection.Build(replacement, command.PlayerId),
            resolution));
    }

    private GameResult<HpDamageResult> ApplyDamageCore(ApplyDamageCommand command)
    {
        if (command.Damage <= 0 || string.IsNullOrWhiteSpace(command.EventKey))
        {
            return GameResult<HpDamageResult>.Failure(GameErrorCode.InvalidDamage);
        }

        if (!stateStore.TryGet(command.RoomId, out var state) || state is null)
        {
            return GameResult<HpDamageResult>.Failure(GameErrorCode.GameNotFound);
        }

        var character = state.Characters.SingleOrDefault(candidate => candidate.CharacterId == command.CharacterId);
        if (character is null)
        {
            return GameResult<HpDamageResult>.Failure(GameErrorCode.CharacterNotFound);
        }

        var input = new HpDamageInput(
            command.EventKey.Trim(),
            command.Damage,
            command.ConRoll ?? diceRoller.RollPercentile(0, 0).SelectedRoll);
        HpDamageResolutionResult resolution;
        try
        {
            resolution = hpDamageEngine.Apply(character.Health, input);
        }
        catch (ArgumentOutOfRangeException)
        {
            return GameResult<HpDamageResult>.Failure(GameErrorCode.InvalidDamage);
        }

        if (!resolution.Changed)
        {
            var snapshot = GameProjection.Build(state, character.OwnerPlayerId);
            return GameResult<HpDamageResult>.Success(new HpDamageResult(snapshot, resolution.Event, resolution.Deduped), changed: false);
        }

        var combat = state.Combat;
        if (combat is { Active: true }
            && !character.Health.Dying
            && resolution.State.Dying
            && combat.Participants.Any(participant => participant.CharacterId == character.CharacterId && participant.Active))
        {
            var schedule = combat.DyingSchedule.ToDictionary(pair => pair.Key, pair => pair.Value);
            schedule[character.CharacterId] = new DyingScheduleState(combat.Round, null);
            combat = combat with { DyingSchedule = schedule };
        }

        var replacement = new MultiplayerGameState(
            state.RoomId,
            state.Revision + 1,
            state.Status,
            state.CreatedAt,
            state.Characters.Select(candidate => candidate.CharacterId == character.CharacterId ? candidate.WithHealth(resolution.State) : candidate),
            state.LastCheck,
            combat);
        if (!stateStore.TryReplace(state, replacement))
        {
            return GameResult<HpDamageResult>.Failure(GameErrorCode.StateConflict);
        }

        return GameResult<HpDamageResult>.Success(
            new HpDamageResult(GameProjection.Build(replacement, character.OwnerPlayerId), resolution.Event, false));
    }

    private GameResult<HealthStabilizationResult> ResolveDyingRoundCore(ResolveDyingRoundCommand command)
    {
        var access = TryGetInternalCharacter(command.RoomId, command.CharacterId, out var state, out var character);
        if (access is not null)
        {
            return GameResult<HealthStabilizationResult>.Failure(access.Value);
        }

        if (!character!.Health.Dying)
        {
            return GameResult<HealthStabilizationResult>.Failure(GameErrorCode.InvalidHealthStabilization);
        }

        var health = character.Health;
        var resolution = ResolveHealth(
            () => healthStabilizationEngine.ResolveDyingRound(
                health,
                new DyingRoundInput(
                    diceRoller.RollPercentile(0, 0).SelectedRoll,
                    command.SourceId,
                    DateTimeOffset.UtcNow)));
        if (resolution.Error is not null)
        {
            return GameResult<HealthStabilizationResult>.Failure(GameErrorCode.InvalidHealthStabilization);
        }

        return CommitHealthStabilization(state!, character, resolution.Value!);
    }

    private GameResult<HealthStabilizationResult> ResolveFirstAidCore(ResolveFirstAidCommand command)
    {
        var access = TryGetInternalCharacter(command.RoomId, command.CharacterId, out var state, out var character);
        if (access is not null)
        {
            return GameResult<HealthStabilizationResult>.Failure(access.Value);
        }

        if (command.Target is < 1 or > 100 || !command.WithinHour)
        {
            return GameResult<HealthStabilizationResult>.Failure(GameErrorCode.InvalidHealthStabilization);
        }

        var health = character!.Health;
        var resolution = ResolveHealth(
            () => healthStabilizationEngine.ResolveFirstAid(
                health,
                new FirstAidInput(
                    command.Target,
                    command.WithinHour,
                    diceRoller.RollPercentile(0, 0).SelectedRoll,
                    command.SourceId,
                    DateTimeOffset.UtcNow)));
        if (resolution.Error is not null)
        {
            return GameResult<HealthStabilizationResult>.Failure(GameErrorCode.InvalidHealthStabilization);
        }

        return CommitHealthStabilization(state!, character!, resolution.Value!);
    }

    private static HealthResolution ResolveHealth(Func<HealthStabilizationResolutionResult> resolve)
    {
        try
        {
            return new HealthResolution(resolve(), null);
        }
        catch (HealthStabilizationRuleException exception)
        {
            return new HealthResolution(null, exception.Error);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new HealthResolution(null, HealthStabilizationError.InvalidTarget);
        }
    }

    private GameResult<HealthStabilizationResult> CommitHealthStabilization(
        MultiplayerGameState state,
        CharacterState character,
        HealthStabilizationResolutionResult resolution)
    {
        if (!resolution.Changed)
        {
            return GameResult<HealthStabilizationResult>.Success(
                new HealthStabilizationResult(
                    GameProjection.Build(state, character.OwnerPlayerId),
                    resolution.DyingCheck,
                    resolution.Treatment),
                changed: false);
        }

        var replacement = new MultiplayerGameState(
            state.RoomId,
            state.Revision + 1,
            state.Status,
            state.CreatedAt,
            state.Characters.Select(candidate => candidate.CharacterId == character.CharacterId ? candidate.WithHealth(resolution.State) : candidate),
            state.LastCheck,
            state.Combat);
        if (!stateStore.TryReplace(state, replacement))
        {
            return GameResult<HealthStabilizationResult>.Failure(GameErrorCode.StateConflict);
        }

        return GameResult<HealthStabilizationResult>.Success(
            new HealthStabilizationResult(
                GameProjection.Build(replacement, character.OwnerPlayerId),
                resolution.DyingCheck,
                resolution.Treatment));
    }

    private GameErrorCode? TryGetInternalCharacter(
        Guid roomId,
        Guid characterId,
        out MultiplayerGameState? state,
        out CharacterState? character)
    {
        state = null;
        character = null;
        if (!roomStore.TryGet(roomId, out var room) || room is null)
        {
            return GameErrorCode.RoomNotFound;
        }

        if (room.Status == RoomStatus.Closed)
        {
            return GameErrorCode.RoomClosed;
        }

        if (!stateStore.TryGet(roomId, out state) || state is null)
        {
            return GameErrorCode.GameNotFound;
        }

        character = state.Characters.SingleOrDefault(candidate => candidate.CharacterId == characterId);
        if (character is null)
        {
            return GameErrorCode.CharacterNotFound;
        }

        var ownerPlayerId = character.OwnerPlayerId;
        return room.Players.Any(player => player.PlayerId == ownerPlayerId)
            ? null
            : GameErrorCode.NotMember;
    }

    private sealed record HealthResolution(HealthStabilizationResolutionResult? Value, HealthStabilizationError? Error);

    private sealed record BeginOpposedExchangeContext(
        MultiplayerGameState State,
        CombatSession Session,
        CombatParticipantState Attacker,
        CombatParticipantState Defender);

    private GameErrorCode? TryGetMember(Guid roomId, Guid playerId, out RoomSession? room)
    {
        room = null;
        if (!roomStore.TryGet(roomId, out room) || room is null)
        {
            return GameErrorCode.RoomNotFound;
        }

        if (room.Status == RoomStatus.Closed)
        {
            return GameErrorCode.RoomClosed;
        }

        return room.Players.Any(player => player.PlayerId == playerId)
            ? null
            : GameErrorCode.NotMember;
    }

    private async Task<T> WithRoomLockAsync<T>(Guid roomId, Func<T> operation)
    {
        var roomLock = gameLocks.GetOrAdd(roomId, _ => new SemaphoreSlim(1, 1));
        await roomLock.WaitAsync();
        try
        {
            return operation();
        }
        finally
        {
            roomLock.Release();
        }
    }

    private async Task<T> WithRoomLockAsync<T>(Guid roomId, Func<Task<T>> operation)
    {
        var roomLock = gameLocks.GetOrAdd(roomId, _ => new SemaphoreSlim(1, 1));
        await roomLock.WaitAsync();
        try
        {
            return await operation();
        }
        finally
        {
            roomLock.Release();
        }
    }
}
