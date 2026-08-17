using Microsoft.AspNetCore.SignalR;

namespace Trpg.Multiplayer.Api.Realtime;

public sealed class SignalRRoomRealtimeNotifier(
    IHubContext<RoomHub, IRoomClient> hubContext,
    IPlayerConnectionRegistry connections) : IRoomRealtimeNotifier
{
    public async Task PublishMemberJoinedAsync(RoomSnapshot snapshot, PlayerSnapshot player)
    {
        var roomClients = hubContext.Clients.Group(RoomGroupNames.For(snapshot.RoomId));
        await roomClients.MemberJoined(new MemberJoinedEvent(
            snapshot.RoomId,
            player,
            snapshot.Revision,
            snapshot));
        await roomClients.RoomSnapshot(snapshot);
    }

    public async Task PublishReadyChangedAsync(RoomSnapshot snapshot, Guid playerId, bool isReady)
    {
        var roomClients = hubContext.Clients.Group(RoomGroupNames.For(snapshot.RoomId));
        await roomClients.ReadyChanged(new ReadyChangedEvent(
            snapshot.RoomId,
            playerId,
            isReady,
            snapshot.Revision,
            snapshot));
        await roomClients.RoomSnapshot(snapshot);
    }

    public async Task PublishMemberLeftAsync(RoomSnapshot snapshot, Guid playerId)
    {
        var groupName = RoomGroupNames.For(snapshot.RoomId);
        var leavingConnections = connections.GetConnections(snapshot.RoomId, playerId);
        foreach (var connectionId in leavingConnections)
        {
            await hubContext.Groups.RemoveFromGroupAsync(connectionId, groupName);
            connections.Unregister(connectionId);
        }

        var remainingClients = hubContext.Clients.Group(groupName);
        await remainingClients.MemberLeft(new MemberLeftEvent(
            snapshot.RoomId,
            playerId,
            snapshot.Revision,
            snapshot));
        await remainingClients.RoomSnapshot(snapshot);
    }

    public async Task PublishMemberConnectionChangedAsync(
        RoomSnapshot snapshot,
        Guid playerId,
        bool isConnected)
    {
        var roomClients = hubContext.Clients.Group(RoomGroupNames.For(snapshot.RoomId));
        await roomClients.MemberConnectionChanged(new MemberConnectionChangedEvent(
            snapshot.RoomId,
            playerId,
            isConnected,
            snapshot.Revision,
            snapshot));
        await roomClients.RoomSnapshot(snapshot);
    }

    public async Task PublishRoomClosedAsync(Guid roomId)
    {
        var groupName = RoomGroupNames.For(roomId);
        var roomConnections = connections.GetRoomConnections(roomId);

        // 修改时间：2026-08-17 10:53:43
        // 修改说明：RoomClosed 投递与 group/registry cleanup 使用嵌套 finally。
        // 修改原因：投递失败不能遗留已关闭房间的连接身份与 group membership。
        // 业务影响：保留原始失败传播，不增加重试、outbox 或持久化语义。
        try
        {
            await hubContext.Clients.Group(groupName).RoomClosed(new RoomClosedEvent(roomId));
        }
        finally
        {
            try
            {
                await Task.WhenAll(roomConnections.Select(connection =>
                    hubContext.Groups.RemoveFromGroupAsync(connection.ConnectionId, groupName)));
            }
            finally
            {
                connections.RemoveRoom(roomId);
            }
        }
    }
}
