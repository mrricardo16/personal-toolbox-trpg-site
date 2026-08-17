namespace Trpg.Multiplayer.Api.Realtime;

public sealed record CheckResolvedEvent(
    Guid RoomId,
    Guid CheckId,
    Guid PlayerId,
    Guid CharacterId,
    string CheckKey,
    long GameRevision);
