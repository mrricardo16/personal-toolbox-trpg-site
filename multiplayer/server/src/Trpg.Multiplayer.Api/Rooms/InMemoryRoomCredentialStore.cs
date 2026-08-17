using System.Collections.Concurrent;

namespace Trpg.Multiplayer.Api.Rooms;

public sealed class InMemoryRoomCredentialStore : IRoomCredentialStore
{
    private readonly ConcurrentDictionary<Guid, string> credentials = new();

    public void Set(Guid roomId, string credential)
    {
        credentials[roomId] = credential;
    }

    public bool TryGet(Guid roomId, out string? credential)
    {
        return credentials.TryGetValue(roomId, out credential);
    }

    public bool Remove(Guid roomId)
    {
        return credentials.TryRemove(roomId, out _);
    }

    public bool Exists(Guid roomId)
    {
        return credentials.ContainsKey(roomId);
    }
}
