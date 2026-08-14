using System.Collections.Concurrent;

namespace Trpg.Multiplayer.Api.Rooms;

public sealed class InMemoryRoomStore : IRoomStore
{
    private readonly ConcurrentDictionary<Guid, RoomSession> rooms = new();

    public bool TryAdd(RoomSession room)
    {
        return rooms.TryAdd(room.RoomId, room);
    }

    public bool TryGet(Guid roomId, out RoomSession? room)
    {
        return rooms.TryGetValue(roomId, out room);
    }

    public bool TryRemove(Guid roomId, out RoomSession? room)
    {
        return rooms.TryRemove(roomId, out room);
    }

    public bool Exists(Guid roomId)
    {
        return rooms.ContainsKey(roomId);
    }
}
