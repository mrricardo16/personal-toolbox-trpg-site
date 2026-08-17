namespace Trpg.Multiplayer.Api.Rooms;

public sealed record CreateRoomCommand(Guid HostPlayerId, string HostNickname, int MaxPlayers);

public sealed record JoinRoomCommand(Guid RoomId, Guid PlayerId, string Nickname);

public sealed record LeaveRoomCommand(Guid RoomId, Guid PlayerId);

public sealed record SetRoomReadyCommand(Guid RoomId, Guid PlayerId, bool IsReady);

public sealed record SetConnectedRoomCommand(Guid RoomId, Guid PlayerId, bool IsConnected);

public sealed record RoomLeaveResult(RoomSession? Room, bool RoomWasClosed);

public sealed class SetRoomAiConfigurationCommand
{
    public SetRoomAiConfigurationCommand(
        Guid roomId,
        Guid playerId,
        string? provider,
        string? endpoint,
        string? model,
        string? credential)
    {
        RoomId = roomId;
        PlayerId = playerId;
        Provider = provider;
        Endpoint = endpoint;
        Model = model;
        Credential = credential;
    }

    public Guid RoomId { get; }

    public Guid PlayerId { get; }

    public string? Provider { get; }

    public string? Endpoint { get; }

    public string? Model { get; }

    public string? Credential { get; }
}

public sealed record RemoveRoomAiCredentialCommand(Guid RoomId, Guid PlayerId);
