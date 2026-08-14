using System.Collections.ObjectModel;

namespace Trpg.Multiplayer.Api.Rooms;

public sealed class RoomSession
{
    public RoomSession(
        Guid roomId,
        Guid hostPlayerId,
        int maxPlayers,
        RoomStatus status,
        long revision,
        DateTimeOffset createdAt,
        IEnumerable<RoomPlayer>? players = null)
    {
        RoomId = roomId;
        HostPlayerId = hostPlayerId;
        MaxPlayers = maxPlayers;
        Status = status;
        Revision = revision;
        CreatedAt = createdAt;
        Players = new ReadOnlyCollection<RoomPlayer>((players ?? []).ToArray());
    }

    public Guid RoomId { get; }

    public Guid HostPlayerId { get; }

    public int MaxPlayers { get; }

    public RoomStatus Status { get; }

    public long Revision { get; }

    public DateTimeOffset CreatedAt { get; }

    public IReadOnlyList<RoomPlayer> Players { get; }
}
