using Microsoft.AspNetCore.SignalR;
using Trpg.Multiplayer.Api.Rooms;

namespace Trpg.Multiplayer.Api.Realtime;

public sealed class RoomHub(
    IPlayerSessionStore playerSessions,
    IRoomStore rooms,
    IInviteCodeRegistry inviteCodes,
    IPlayerConnectionRegistry connections) : Hub<IRoomClient>
{
    private const string AttachRejectedMessage = "Session attach rejected.";

    public async Task<RoomSnapshot> AttachSession(string playerSessionToken)
    {
        if (string.IsNullOrWhiteSpace(playerSessionToken)
            || !playerSessions.TryGet(playerSessionToken, out var session)
            || session is null
            || !rooms.TryGet(session.RoomId, out var room)
            || room is null
            || !room.Players.Any(player => player.PlayerId == session.PlayerId))
        {
            throw new HubException(AttachRejectedMessage);
        }

        try
        {
            connections.Register(Context.ConnectionId, session.RoomId, session.PlayerId);
        }
        catch (InvalidOperationException)
        {
            throw new HubException(AttachRejectedMessage);
        }

        try
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, RoomGroupNames.For(session.RoomId));
        }
        catch
        {
            connections.Unregister(Context.ConnectionId);
            throw;
        }

        return RoomSnapshotMapper.ToSnapshot(room, inviteCodes);
    }
}
