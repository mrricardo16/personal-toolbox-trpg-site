using Microsoft.AspNetCore.SignalR;
using Trpg.Multiplayer.Api.Rooms;

namespace Trpg.Multiplayer.Api.Realtime;

public sealed class RoomHub(
    IPlayerSessionStore playerSessions,
    IRoomStore rooms,
    IInviteCodeRegistry inviteCodes,
    IPlayerConnectionRegistry connections,
    RoomCoordinator coordinator,
    IRoomRealtimeNotifier notifier,
    RoomMutationDeliveryGate mutationGate,
    ILogger<RoomHub> logger) : Hub<IRoomClient>
{
    private const string AttachRejectedMessage = "Session attach rejected.";

    public async Task<RoomSnapshot> AttachSession(string playerSessionToken)
    {
        if (string.IsNullOrWhiteSpace(playerSessionToken)
            || !playerSessions.TryGet(playerSessionToken, out var session)
            || session is null)
        {
            throw new HubException(AttachRejectedMessage);
        }

        // 修改时间：2026-08-17 12:08:19
        // 修改说明：Attach 的重新校验、registry/group、connected mutation 与 notifier 共享房间 delivery gate。
        // 修改原因：避免 HTTP mutation、last disconnect 与 first attach 交错，并防止 non-first 返回 gate 外预读的旧快照。
        // 业务影响：同房间 lifecycle 串行化；不同房间仍可并发，RoomCoordinator 仍是唯一 Room mutation authority。
        return await mutationGate.RunAsync(session.RoomId, async () =>
        {
            if (!rooms.TryGet(session.RoomId, out var room)
                || room is null
                || !room.Players.Any(player => player.PlayerId == session.PlayerId))
            {
                await CleanupRejectedAttachAsync(session.RoomId);
                throw new HubException(AttachRejectedMessage);
            }

            bool isFirstConnection;
            try
            {
                isFirstConnection = connections.Register(
                    Context.ConnectionId,
                    session.RoomId,
                    session.PlayerId);
            }
            catch (InvalidOperationException)
            {
                throw new HubException(AttachRejectedMessage);
            }

            try
            {
                await Groups.AddToGroupAsync(
                    Context.ConnectionId,
                    RoomGroupNames.For(session.RoomId));
            }
            catch
            {
                await CleanupRejectedAttachAsync(session.RoomId);
                throw;
            }

            if (!isFirstConnection)
            {
                return RoomSnapshotMapper.ToSnapshot(room, inviteCodes);
            }

            var result = await coordinator.SetConnectedAsync(
                new SetConnectedRoomCommand(session.RoomId, session.PlayerId, true));
            if (!result.IsSuccess)
            {
                await CleanupRejectedAttachAsync(session.RoomId);
                throw new HubException(AttachRejectedMessage);
            }

            var snapshot = RoomSnapshotMapper.ToSnapshot(result.Value!, inviteCodes);
            if (result.Changed)
            {
                await notifier.PublishMemberConnectionChangedAsync(snapshot, session.PlayerId, true);
            }

            return snapshot;
        });
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            if (!connections.TryGetConnection(Context.ConnectionId, out var registeredIdentity)
                || registeredIdentity is null)
            {
                return;
            }

            // 修改时间：2026-08-17 12:08:19
            // 修改说明：disconnect 的 Unregister/last 判定、group remove、connected mutation 与 notifier 共享房间 gate。
            // 修改原因：阻止旧 last-disconnect 在新 first-attach 之后写回 canonical false。
            // 业务影响：保持 first/last 语义与投递顺序一致，不改变 Room membership 或 readiness。
            await mutationGate.RunAsync(registeredIdentity.RoomId, async () =>
            {
                var unregistration = connections.Unregister(Context.ConnectionId);
                if (unregistration is null)
                {
                    return false;
                }

                try
                {
                    await Groups.RemoveFromGroupAsync(
                        Context.ConnectionId,
                        RoomGroupNames.For(unregistration.Identity.RoomId));
                }
                catch (Exception groupRemovalException)
                {
                    logger.LogWarning(
                        groupRemovalException,
                        "Failed to remove disconnected connection from room group. RoomId: {RoomId}, ConnectionId: {ConnectionId}",
                        unregistration.Identity.RoomId,
                        Context.ConnectionId);
                }

                if (!unregistration.IsLastConnection)
                {
                    return true;
                }

                await SetLastConnectionDisconnectedAsync(unregistration.Identity);
                return true;
            });
        }
        finally
        {
            await base.OnDisconnectedAsync(exception);
        }
    }

    private async Task CleanupRejectedAttachAsync(Guid roomId)
    {
        if (connections.TryGetConnection(Context.ConnectionId, out var existingIdentity)
            && existingIdentity is not null
            && existingIdentity.RoomId != roomId)
        {
            return;
        }

        try
        {
            await Groups.RemoveFromGroupAsync(
                Context.ConnectionId,
                RoomGroupNames.For(roomId));
        }
        catch (Exception groupRemovalException)
        {
            logger.LogWarning(
                groupRemovalException,
                "Failed to remove rejected connection from room group. RoomId: {RoomId}, ConnectionId: {ConnectionId}",
                roomId,
                Context.ConnectionId);
        }
        finally
        {
            connections.Unregister(Context.ConnectionId);
        }
    }

    private async Task SetLastConnectionDisconnectedAsync(ConnectionIdentity identity)
    {
        var result = await coordinator.SetConnectedAsync(
            new SetConnectedRoomCommand(identity.RoomId, identity.PlayerId, false));
        if (result.IsSuccess)
        {
            if (result.Changed)
            {
                var snapshot = RoomSnapshotMapper.ToSnapshot(result.Value!, inviteCodes);
                await notifier.PublishMemberConnectionChangedAsync(snapshot, identity.PlayerId, false);
            }

            return;
        }

        if (result.Error!.Code is not RoomErrorCode.RoomNotFound and not RoomErrorCode.NotMember)
        {
            throw new InvalidOperationException("Connection cleanup failed.");
        }
    }
}
