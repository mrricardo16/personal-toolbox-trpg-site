namespace Trpg.Multiplayer.Api.Realtime;

public interface IPlayerConnectionRegistry
{
    bool Register(string connectionId, Guid roomId, Guid playerId);

    bool TryGetConnection(string connectionId, out ConnectionIdentity? identity);

    ConnectionUnregistration? Unregister(string connectionId);

    IReadOnlyCollection<string> GetConnections(Guid roomId, Guid playerId);

    IReadOnlyCollection<ConnectionIdentity> GetRoomConnections(Guid roomId);

    IReadOnlyCollection<ConnectionIdentity> RemoveRoom(Guid roomId);
}

public sealed record ConnectionIdentity(string ConnectionId, Guid RoomId, Guid PlayerId);

public sealed record ConnectionUnregistration(ConnectionIdentity Identity, bool IsLastConnection);
