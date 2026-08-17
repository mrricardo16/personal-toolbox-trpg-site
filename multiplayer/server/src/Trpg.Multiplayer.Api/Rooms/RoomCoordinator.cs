using System.Collections.Concurrent;

namespace Trpg.Multiplayer.Api.Rooms;

public sealed class RoomCoordinator(IRoomStore roomStore)
{
    private const long InitialRevision = 1;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> roomLocks = new();

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
            [new RoomPlayer(command.HostPlayerId, command.HostNickname, true, false, true)]);

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

        var updatedRoom = CopyRoom(room, room.Players.Append(new RoomPlayer(command.PlayerId, command.Nickname, false, false, true)));
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
            return roomStore.TryRemove(room.RoomId, out _)
                ? RoomResult<RoomLeaveResult>.Success(new RoomLeaveResult(null, true))
                : RoomResult<RoomLeaveResult>.Failure(RoomErrorCode.RoomNotFound);
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
        return new RoomSession(
            room.RoomId,
            room.HostPlayerId,
            room.MaxPlayers,
            room.Status,
            room.Revision + 1,
            room.CreatedAt,
            players);
    }
}
