namespace Trpg.Multiplayer.Api.Rooms;

public interface IRoomCredentialStore
{
    void Set(Guid roomId, string credential);

    bool TryGet(Guid roomId, out string? credential);

    bool Remove(Guid roomId);

    bool Exists(Guid roomId);
}
