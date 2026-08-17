namespace Trpg.Multiplayer.Api.Realtime;

public interface IRoomRealtimeNotifier
{
    Task PublishMemberJoinedAsync(RoomSnapshot snapshot, PlayerSnapshot player);

    Task PublishReadyChangedAsync(RoomSnapshot snapshot, Guid playerId, bool isReady);

    Task PublishMemberLeftAsync(RoomSnapshot snapshot, Guid playerId);

    Task PublishRoomClosedAsync(Guid roomId);
}
