namespace Trpg.Multiplayer.Api.Realtime;

public interface IRoomRealtimeNotifier
{
    Task PublishMemberJoinedAsync(RoomSnapshot snapshot, PlayerSnapshot player);

    Task PublishReadyChangedAsync(RoomSnapshot snapshot, Guid playerId, bool isReady);

    Task PublishMemberLeftAsync(RoomSnapshot snapshot, Guid playerId);

    Task PublishMemberConnectionChangedAsync(RoomSnapshot snapshot, Guid playerId, bool isConnected);

    Task PublishRoomClosedAsync(Guid roomId);
}
