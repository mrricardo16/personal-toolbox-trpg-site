namespace Trpg.Multiplayer.Api.Rooms;

public sealed record RoomPlayer(
    Guid PlayerId,
    string Nickname,
    bool IsHost = false,
    bool IsReady = false,
    bool IsConnected = false);
