namespace Trpg.Multiplayer.Api.Rooms;

public interface IRoomStore
{
    bool TryAdd(RoomSession room);

    bool TryGet(Guid roomId, out RoomSession? room);

    bool TryRemove(Guid roomId, out RoomSession? room);

    bool Exists(Guid roomId);
}
