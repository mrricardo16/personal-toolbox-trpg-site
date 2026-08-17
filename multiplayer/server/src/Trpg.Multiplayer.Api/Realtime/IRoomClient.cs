namespace Trpg.Multiplayer.Api.Realtime;

public interface IRoomClient
{
    Task RoomSnapshot(RoomSnapshot snapshot);

    Task MemberJoined(MemberJoinedEvent message);

    Task MemberLeft(MemberLeftEvent message);

    Task ReadyChanged(ReadyChangedEvent message);

    Task MemberConnectionChanged(MemberConnectionChangedEvent message);

    Task RoomClosed(RoomClosedEvent message);
}
