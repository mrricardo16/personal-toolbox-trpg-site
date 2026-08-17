namespace Trpg.Multiplayer.Api.Realtime;

public interface IGameRealtimeNotifier
{
    Task PublishGameSnapshotAsync(Guid roomId);

    Task PublishCheckResolvedAsync(Guid roomId, CheckResolvedEvent message);

    Task SendGameSnapshotAsync(string connectionId, Guid roomId, Guid playerId);
}
