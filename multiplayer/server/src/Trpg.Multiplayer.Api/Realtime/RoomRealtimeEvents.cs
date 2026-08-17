namespace Trpg.Multiplayer.Api.Realtime;

public sealed record MemberJoinedEvent(
    Guid RoomId,
    PlayerSnapshot Player,
    long Revision,
    RoomSnapshot Snapshot);

public sealed record MemberLeftEvent(
    Guid RoomId,
    Guid PlayerId,
    long Revision,
    RoomSnapshot Snapshot);

public sealed record ReadyChangedEvent(
    Guid RoomId,
    Guid PlayerId,
    bool IsReady,
    long Revision,
    RoomSnapshot Snapshot);

public sealed record RoomClosedEvent(Guid RoomId);
