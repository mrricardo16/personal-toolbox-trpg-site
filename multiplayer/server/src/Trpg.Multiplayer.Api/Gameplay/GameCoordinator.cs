using System.Collections.Concurrent;
using Trpg.Multiplayer.Api.Realtime;
using Trpg.Multiplayer.Api.Rooms;

namespace Trpg.Multiplayer.Api.Gameplay;

public sealed class GameCoordinator : IGameCoordinator
{
    private const long InitialRevision = 1;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> gameLocks = new();
    private readonly IRoomStore roomStore;
    private readonly IGameStateStore stateStore;
    private readonly IDiceRoller diceRoller;
    private readonly ICheckResolutionEngine checkEngine;
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
            record);
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

        var replacement = new MultiplayerGameState(
            state.RoomId,
            state.Revision + 1,
            state.Status,
            state.CreatedAt,
            state.Characters.Select(candidate => candidate.CharacterId == character.CharacterId ? candidate.WithHealth(resolution.State) : candidate),
            state.LastCheck);
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
            state.LastCheck);
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
