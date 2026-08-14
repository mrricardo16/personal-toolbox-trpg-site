namespace Trpg.Multiplayer.Api.Rooms;

public interface IInviteCodeRegistry
{
    bool TryRegister(Guid roomId, out string inviteCode);

    bool TryGetRoomId(string inviteCode, out Guid roomId);

    bool TryGetInviteCode(Guid roomId, out string? inviteCode);

    void Remove(Guid roomId);
}
