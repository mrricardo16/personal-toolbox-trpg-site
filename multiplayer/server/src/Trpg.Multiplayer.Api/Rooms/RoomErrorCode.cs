namespace Trpg.Multiplayer.Api.Rooms;

public enum RoomErrorCode
{
    RoomNotFound,
    RoomClosed,
    RoomFull,
    PlayerAlreadyExists,
    PlayerNotFound,
    InvalidNickname,
    InvalidMaxPlayers,
    InvalidAiConfiguration,
    AiConfigurationNotFound,
    NotMember,
    NotHost
}
