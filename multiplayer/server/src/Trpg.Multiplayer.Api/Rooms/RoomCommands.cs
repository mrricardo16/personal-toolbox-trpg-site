namespace Trpg.Multiplayer.Api.Rooms;

public sealed record CreateRoomCommand(Guid HostPlayerId, string HostNickname, int MaxPlayers);

public sealed record JoinRoomCommand(Guid RoomId, Guid PlayerId, string Nickname);

public sealed record LeaveRoomCommand(Guid RoomId, Guid PlayerId);

public sealed record SetRoomReadyCommand(Guid RoomId, Guid PlayerId, bool IsReady);

public sealed record SetConnectedRoomCommand(Guid RoomId, Guid PlayerId, bool IsConnected);

public sealed record RoomLeaveResult(RoomSession? Room, bool RoomWasClosed);
