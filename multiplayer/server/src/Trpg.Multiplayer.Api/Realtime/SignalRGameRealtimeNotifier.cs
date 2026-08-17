using Microsoft.AspNetCore.SignalR;
using Trpg.Multiplayer.Api.Gameplay;

namespace Trpg.Multiplayer.Api.Realtime;

public sealed class SignalRGameRealtimeNotifier(
    IHubContext<RoomHub, IRoomClient> hubContext,
    IGameStateStore states,
    IPlayerConnectionRegistry connections) : IGameRealtimeNotifier
{
    public async Task PublishGameSnapshotAsync(Guid roomId)
    {
        if (!states.TryGet(roomId, out var state) || state is null)
        {
            return;
        }

        foreach (var connection in connections.GetRoomConnections(roomId))
        {
            await hubContext.Clients
                .Client(connection.ConnectionId)
                .GameSnapshot(GameProjection.Build(state, connection.PlayerId));
        }
    }

    public Task PublishCheckResolvedAsync(Guid roomId, CheckResolvedEvent message)
    {
        var connectionIds = connections
            .GetRoomConnections(roomId)
            .Select(connection => connection.ConnectionId)
            .ToArray();
        return connectionIds.Length == 0
            ? Task.CompletedTask
            : hubContext.Clients.Clients(connectionIds).CheckResolved(message);
    }

    public async Task SendGameSnapshotAsync(string connectionId, Guid roomId, Guid playerId)
    {
        if (!states.TryGet(roomId, out var state) || state is null)
        {
            return;
        }

        await hubContext.Clients
            .Client(connectionId)
            .GameSnapshot(GameProjection.Build(state, playerId));
    }
}
