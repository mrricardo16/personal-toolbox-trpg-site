using System.Collections.Concurrent;

namespace Trpg.Multiplayer.Api.Rooms;

public sealed class RoomCoordinator
{
    private const long InitialRevision = 1;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> roomLocks = new();
    private readonly IRoomStore roomStore;
    private readonly IRoomCredentialStore credentialStore;

    public RoomCoordinator(IRoomStore roomStore)
        : this(roomStore, new InMemoryRoomCredentialStore())
    {
    }

    public RoomCoordinator(IRoomStore roomStore, IRoomCredentialStore credentialStore)
    {
        this.roomStore = roomStore;
        this.credentialStore = credentialStore;
    }

    public Task<RoomResult<RoomSession>> CreateAsync(CreateRoomCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.HostNickname))
        {
            return Task.FromResult(RoomResult<RoomSession>.Failure(RoomErrorCode.InvalidNickname));
        }

        if (command.MaxPlayers <= 0)
        {
            return Task.FromResult(RoomResult<RoomSession>.Failure(RoomErrorCode.InvalidMaxPlayers));
        }

        var room = new RoomSession(
            Guid.NewGuid(),
            command.HostPlayerId,
            command.MaxPlayers,
            RoomStatus.Lobby,
            InitialRevision,
            DateTimeOffset.UtcNow,
            [new RoomPlayer(command.HostPlayerId, command.HostNickname, true, false, false)]);

        return Task.FromResult(roomStore.TryAdd(room)
            ? RoomResult<RoomSession>.Success(room)
            : RoomResult<RoomSession>.Failure(RoomErrorCode.RoomNotFound));
    }

    public Task<RoomResult<RoomSession>> GetAsync(Guid roomId)
    {
        return Task.FromResult(roomStore.TryGet(roomId, out var room)
            ? RoomResult<RoomSession>.Success(room!)
            : RoomResult<RoomSession>.Failure(RoomErrorCode.RoomNotFound));
    }

    public Task<RoomResult<RoomSession>> JoinAsync(JoinRoomCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Nickname))
        {
            return Task.FromResult(RoomResult<RoomSession>.Failure(RoomErrorCode.InvalidNickname));
        }

        return WithRoomLockAsync(command.RoomId, () => JoinCore(command));
    }

    public Task<RoomResult<RoomLeaveResult>> LeaveAsync(LeaveRoomCommand command)
    {
        return WithRoomLockAsync(command.RoomId, () => LeaveCore(command));
    }

    public Task<RoomResult<RoomSession>> SetReadyAsync(SetRoomReadyCommand command)
    {
        return WithRoomLockAsync(command.RoomId, () => SetReadyCore(command));
    }

    public Task<RoomResult<RoomSession>> SetConnectedAsync(SetConnectedRoomCommand command)
    {
        return WithRoomLockAsync(command.RoomId, () => SetConnectedCore(command));
    }

    public Task<RoomResult<RoomSession>> SetAiConfigurationAsync(SetRoomAiConfigurationCommand command)
    {
        if (!TryBuildAiConfiguration(command, credentialPresent: true, out _))
        {
            return Task.FromResult(RoomResult<RoomSession>.Failure(RoomErrorCode.InvalidAiConfiguration));
        }

        return WithRoomLockAsync(command.RoomId, () => SetAiConfigurationCore(command));
    }

    public Task<RoomResult<RoomSession>> RemoveAiCredentialAsync(RemoveRoomAiCredentialCommand command)
    {
        return WithRoomLockAsync(command.RoomId, () => RemoveAiCredentialCore(command));
    }

    private RoomResult<RoomSession> JoinCore(JoinRoomCommand command)
    {
        if (!roomStore.TryGet(command.RoomId, out var room))
        {
            return RoomResult<RoomSession>.Failure(RoomErrorCode.RoomNotFound);
        }

        if (room!.Status == RoomStatus.Closed)
        {
            return RoomResult<RoomSession>.Failure(RoomErrorCode.RoomClosed);
        }

        if (room.Players.Any(player => player.PlayerId == command.PlayerId))
        {
            return RoomResult<RoomSession>.Failure(RoomErrorCode.PlayerAlreadyExists);
        }

        if (room.Players.Count >= room.MaxPlayers)
        {
            return RoomResult<RoomSession>.Failure(RoomErrorCode.RoomFull);
        }

        var updatedRoom = CopyRoom(room, room.Players.Append(new RoomPlayer(command.PlayerId, command.Nickname, false, false, false)));
        return roomStore.TryReplace(room, updatedRoom)
            ? RoomResult<RoomSession>.Success(updatedRoom)
            : RoomResult<RoomSession>.Failure(RoomErrorCode.RoomNotFound);
    }

    private RoomResult<RoomLeaveResult> LeaveCore(LeaveRoomCommand command)
    {
        if (!roomStore.TryGet(command.RoomId, out var room))
        {
            return RoomResult<RoomLeaveResult>.Failure(RoomErrorCode.RoomNotFound);
        }

        if (room!.Status == RoomStatus.Closed)
        {
            return RoomResult<RoomLeaveResult>.Failure(RoomErrorCode.RoomClosed);
        }

        if (!room.Players.Any(player => player.PlayerId == command.PlayerId))
        {
            return RoomResult<RoomLeaveResult>.Failure(RoomErrorCode.PlayerNotFound);
        }

        if (room.HostPlayerId == command.PlayerId)
        {
            if (!roomStore.TryRemove(room.RoomId, out _))
            {
                return RoomResult<RoomLeaveResult>.Failure(RoomErrorCode.RoomNotFound);
            }

            credentialStore.Remove(room.RoomId);
            return RoomResult<RoomLeaveResult>.Success(new RoomLeaveResult(null, true));
        }

        var updatedRoom = CopyRoom(room, room.Players.Where(player => player.PlayerId != command.PlayerId));
        return roomStore.TryReplace(room, updatedRoom)
            ? RoomResult<RoomLeaveResult>.Success(new RoomLeaveResult(updatedRoom, false))
            : RoomResult<RoomLeaveResult>.Failure(RoomErrorCode.RoomNotFound);
    }

    private RoomResult<RoomSession> SetReadyCore(SetRoomReadyCommand command)
    {
        if (!roomStore.TryGet(command.RoomId, out var room))
        {
            return RoomResult<RoomSession>.Failure(RoomErrorCode.RoomNotFound);
        }

        if (room!.Status == RoomStatus.Closed)
        {
            return RoomResult<RoomSession>.Failure(RoomErrorCode.RoomClosed);
        }

        var player = room.Players.SingleOrDefault(candidate => candidate.PlayerId == command.PlayerId);
        if (player is null)
        {
            return RoomResult<RoomSession>.Failure(RoomErrorCode.NotMember);
        }

        if (player.IsReady == command.IsReady)
        {
            return RoomResult<RoomSession>.Success(room, changed: false);
        }

        var updatedPlayers = room.Players.Select(candidate => candidate.PlayerId == command.PlayerId
            ? candidate with { IsReady = command.IsReady }
            : candidate);
        var updatedRoom = CopyRoom(room, updatedPlayers);
        return roomStore.TryReplace(room, updatedRoom)
            ? RoomResult<RoomSession>.Success(updatedRoom)
            : RoomResult<RoomSession>.Failure(RoomErrorCode.RoomNotFound);
    }

    private RoomResult<RoomSession> SetConnectedCore(SetConnectedRoomCommand command)
    {
        if (!roomStore.TryGet(command.RoomId, out var room))
        {
            return RoomResult<RoomSession>.Failure(RoomErrorCode.RoomNotFound);
        }

        if (room!.Status == RoomStatus.Closed)
        {
            return RoomResult<RoomSession>.Failure(RoomErrorCode.RoomClosed);
        }

        var player = room.Players.SingleOrDefault(candidate => candidate.PlayerId == command.PlayerId);
        if (player is null)
        {
            return RoomResult<RoomSession>.Failure(RoomErrorCode.NotMember);
        }

        if (player.IsConnected == command.IsConnected)
        {
            return RoomResult<RoomSession>.Success(room, changed: false);
        }

        var updatedPlayers = room.Players.Select(candidate => candidate.PlayerId == command.PlayerId
            ? candidate with { IsConnected = command.IsConnected }
            : candidate);
        var updatedRoom = CopyRoom(room, updatedPlayers);
        return roomStore.TryReplace(room, updatedRoom)
            ? RoomResult<RoomSession>.Success(updatedRoom)
            : RoomResult<RoomSession>.Failure(RoomErrorCode.RoomNotFound);
    }

    private RoomResult<RoomSession> SetAiConfigurationCore(SetRoomAiConfigurationCommand command)
    {
        if (!roomStore.TryGet(command.RoomId, out var room))
        {
            return RoomResult<RoomSession>.Failure(RoomErrorCode.RoomNotFound);
        }

        var currentRoom = room!;
        var authorizationError = GetHostAuthorizationError(currentRoom, command.PlayerId);
        if (authorizationError is not null)
        {
            return RoomResult<RoomSession>.Failure(authorizationError.Value);
        }

        var credentialProvided = command.Credential is not null;
        var credentialPresent = credentialProvided || credentialStore.Exists(command.RoomId);
        if (!TryBuildAiConfiguration(command, credentialPresent, out var configuration))
        {
            return RoomResult<RoomSession>.Failure(RoomErrorCode.InvalidAiConfiguration);
        }

        if (!credentialProvided && currentRoom.AiConfiguration == configuration)
        {
            return RoomResult<RoomSession>.Success(currentRoom, changed: false);
        }

        var updatedRoom = CopyRoom(currentRoom, currentRoom.Players, configuration);
        if (!roomStore.TryReplace(currentRoom, updatedRoom))
        {
            return RoomResult<RoomSession>.Failure(RoomErrorCode.RoomNotFound);
        }

        // 修改时间：2026-08-17 13:00:03
        // 修改说明：public Room metadata 成功替换后，再在同一房间锁内设置 server-private credential。
        // 修改原因：credential 不得进入 RoomSession，同时对外返回前必须完成对应 secret lifecycle。
        // 业务影响：新 credential 始终替换旧值；相同 public config 的替换仍推进 revision。
        if (credentialProvided)
        {
            credentialStore.Set(command.RoomId, command.Credential!);
        }

        return RoomResult<RoomSession>.Success(updatedRoom);
    }

    private RoomResult<RoomSession> RemoveAiCredentialCore(RemoveRoomAiCredentialCommand command)
    {
        if (!roomStore.TryGet(command.RoomId, out var room))
        {
            return RoomResult<RoomSession>.Failure(RoomErrorCode.RoomNotFound);
        }

        var authorizationError = GetHostAuthorizationError(room!, command.PlayerId);
        if (authorizationError is not null)
        {
            return RoomResult<RoomSession>.Failure(authorizationError.Value);
        }

        if (room!.AiConfiguration is null)
        {
            return RoomResult<RoomSession>.Failure(RoomErrorCode.AiConfigurationNotFound);
        }

        var credentialExists = credentialStore.Exists(command.RoomId);
        if (!room.AiConfiguration.CredentialPresent && !credentialExists)
        {
            return RoomResult<RoomSession>.Success(room, changed: false);
        }

        var updatedConfiguration = room.AiConfiguration with { CredentialPresent = false };
        var updatedRoom = CopyRoom(room, room.Players, updatedConfiguration);
        if (!roomStore.TryReplace(room, updatedRoom))
        {
            return RoomResult<RoomSession>.Failure(RoomErrorCode.RoomNotFound);
        }

        credentialStore.Remove(command.RoomId);
        return RoomResult<RoomSession>.Success(updatedRoom);
    }

    private static RoomErrorCode? GetHostAuthorizationError(RoomSession room, Guid playerId)
    {
        if (room.Status == RoomStatus.Closed)
        {
            return RoomErrorCode.RoomClosed;
        }

        var player = room.Players.SingleOrDefault(candidate => candidate.PlayerId == playerId);
        if (player is null)
        {
            return RoomErrorCode.NotMember;
        }

        return room.HostPlayerId == playerId && player.IsHost
            ? null
            : RoomErrorCode.NotHost;
    }

    private static bool TryBuildAiConfiguration(
        SetRoomAiConfigurationCommand command,
        bool credentialPresent,
        out RoomAiConfiguration? configuration)
    {
        configuration = null;
        if (string.IsNullOrWhiteSpace(command.Provider)
            || string.IsNullOrWhiteSpace(command.Model)
            || command.Credential is not null && string.IsNullOrWhiteSpace(command.Credential))
        {
            return false;
        }

        var provider = command.Provider.Trim().ToLowerInvariant();
        if (provider is not RoomAiProviders.DeepSeek and not RoomAiProviders.OpenAiCompatible)
        {
            return false;
        }

        var endpoint = command.Endpoint?.Trim();
        if (provider == RoomAiProviders.DeepSeek && string.IsNullOrWhiteSpace(endpoint))
        {
            endpoint = RoomAiProviders.DeepSeekEndpoint;
        }

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return false;
        }

        configuration = new RoomAiConfiguration(
            provider,
            endpoint,
            command.Model.Trim(),
            credentialPresent);
        return true;
    }

    private async Task<RoomResult<T>> WithRoomLockAsync<T>(Guid roomId, Func<RoomResult<T>> operation)
    {
        var roomLock = roomLocks.GetOrAdd(roomId, _ => new SemaphoreSlim(1, 1));
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

    private static RoomSession CopyRoom(RoomSession room, IEnumerable<RoomPlayer> players)
    {
        return CopyRoom(room, players, room.AiConfiguration);
    }

    private static RoomSession CopyRoom(
        RoomSession room,
        IEnumerable<RoomPlayer> players,
        RoomAiConfiguration? aiConfiguration)
    {
        return new RoomSession(
            room.RoomId,
            room.HostPlayerId,
            room.MaxPlayers,
            room.Status,
            room.Revision + 1,
            room.CreatedAt,
            players,
            aiConfiguration);
    }
}
