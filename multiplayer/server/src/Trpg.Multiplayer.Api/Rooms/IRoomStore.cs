namespace Trpg.Multiplayer.Api.Rooms;

public interface IRoomStore
{
    bool TryAdd(RoomSession room);

    bool TryGet(Guid roomId, out RoomSession? room);

    bool TryReplace(RoomSession expectedRoom, RoomSession replacementRoom);

    bool TryRemove(Guid roomId, out RoomSession? room);

    bool Exists(Guid roomId);
}
