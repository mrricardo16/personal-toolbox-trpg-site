using System.Collections.Concurrent;
using Trpg.Multiplayer.Api.Rooms;

namespace Trpg.Multiplayer.Api.Gameplay;

public sealed class GameCoordinator : IGameCoordinator
{
    private const long InitialRevision = 1;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> gameLocks = new();
    private readonly IRoomStore roomStore;
    private readonly IGameStateStore stateStore;

    public GameCoordinator(IRoomStore roomStore, IGameStateStore stateStore)
    {
        this.roomStore = roomStore;
        this.stateStore = stateStore;
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
                || requested.CheckValues.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value is < 1 or > 100))
            {
                return GameResult<MultiplayerGameState>.Failure(GameErrorCode.InvalidRoster);
            }

            characters.Add(new CharacterState(
                Guid.NewGuid(),
                requested.PlayerId,
                requested.Name.Trim(),
                requested.CheckValues));
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
}
