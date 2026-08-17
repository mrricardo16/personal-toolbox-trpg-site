using System.Collections.Concurrent;

namespace Trpg.Multiplayer.Api.Realtime;

public sealed class InMemoryPlayerConnectionRegistry : IPlayerConnectionRegistry
{
    private readonly object syncRoot = new();
    private readonly ConcurrentDictionary<string, ConnectionIdentity> identitiesByConnection =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<PlayerKey, HashSet<string>> connectionsByPlayer = new();

    public bool Register(string connectionId, Guid roomId, Guid playerId)
    {
        var identity = new ConnectionIdentity(connectionId, roomId, playerId);

        lock (syncRoot)
        {
            if (identitiesByConnection.TryGetValue(connectionId, out var existingIdentity))
            {
                if (existingIdentity == identity)
                {
                    return false;
                }

                throw new InvalidOperationException("The connection is already registered to another player session.");
            }

            var playerKey = new PlayerKey(roomId, playerId);
            var connections = connectionsByPlayer.GetOrAdd(
                playerKey,
                static _ => new HashSet<string>(StringComparer.Ordinal));
            var isFirstConnection = connections.Count == 0;

            identitiesByConnection[connectionId] = identity;
            connections.Add(connectionId);

            return isFirstConnection;
        }
    }

    public ConnectionUnregistration? Unregister(string connectionId)
    {
        lock (syncRoot)
        {
            if (!identitiesByConnection.TryRemove(connectionId, out var identity))
            {
                return null;
            }

            var playerKey = new PlayerKey(identity.RoomId, identity.PlayerId);
            if (!connectionsByPlayer.TryGetValue(playerKey, out var connections))
            {
                return new ConnectionUnregistration(identity, true);
            }

            connections.Remove(connectionId);
            var isLastConnection = connections.Count == 0;
            if (isLastConnection)
            {
                connectionsByPlayer.TryRemove(playerKey, out _);
            }

            return new ConnectionUnregistration(identity, isLastConnection);
        }
    }

    public bool TryGetConnection(string connectionId, out ConnectionIdentity? identity)
    {
        lock (syncRoot)
        {
            return identitiesByConnection.TryGetValue(connectionId, out identity);
        }
    }

    public IReadOnlyCollection<string> GetConnections(Guid roomId, Guid playerId)
    {
        lock (syncRoot)
        {
            return connectionsByPlayer.TryGetValue(new PlayerKey(roomId, playerId), out var connections)
                ? connections.ToArray()
                : [];
        }
    }

    public IReadOnlyCollection<ConnectionIdentity> GetRoomConnections(Guid roomId)
    {
        lock (syncRoot)
        {
            return identitiesByConnection.Values
                .Where(identity => identity.RoomId == roomId)
                .ToArray();
        }
    }

    public IReadOnlyCollection<ConnectionIdentity> RemoveRoom(Guid roomId)
    {
        lock (syncRoot)
        {
            var removedIdentities = identitiesByConnection.Values
                .Where(identity => identity.RoomId == roomId)
                .ToArray();

            foreach (var identity in removedIdentities)
            {
                identitiesByConnection.TryRemove(identity.ConnectionId, out _);

                var playerKey = new PlayerKey(identity.RoomId, identity.PlayerId);
                if (!connectionsByPlayer.TryGetValue(playerKey, out var connections))
                {
                    continue;
                }

                connections.Remove(identity.ConnectionId);
                if (connections.Count == 0)
                {
                    connectionsByPlayer.TryRemove(playerKey, out _);
                }
            }

            return removedIdentities;
        }
    }

    private readonly record struct PlayerKey(Guid RoomId, Guid PlayerId);
}
