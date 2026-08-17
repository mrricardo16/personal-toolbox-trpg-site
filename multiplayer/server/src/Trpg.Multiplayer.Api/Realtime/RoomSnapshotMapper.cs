using Trpg.Multiplayer.Api.Rooms;

namespace Trpg.Multiplayer.Api.Realtime;

public static class RoomSnapshotMapper
{
    public static RoomSnapshot ToSnapshot(RoomSession room, IInviteCodeRegistry inviteCodes)
    {
        var inviteCode = inviteCodes.TryGetInviteCode(room.RoomId, out var registeredInviteCode)
            ? registeredInviteCode!
            : string.Empty;

        return new RoomSnapshot(
            room.RoomId,
            inviteCode,
            room.HostPlayerId,
            room.MaxPlayers,
            room.Status.ToString(),
            room.Revision,
            room.Players
                .Select(player => new PlayerSnapshot(
                    player.PlayerId,
                    player.Nickname,
                    player.IsHost,
                    player.IsReady,
                    player.IsConnected))
                .ToArray());
    }
}
