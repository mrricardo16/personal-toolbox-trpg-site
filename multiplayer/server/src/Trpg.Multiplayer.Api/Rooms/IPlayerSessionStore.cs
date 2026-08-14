namespace Trpg.Multiplayer.Api.Rooms;

public interface IPlayerSessionStore
{
    string Create(Guid playerId, Guid roomId, bool isHost);

    bool TryGet(string token, out PlayerSessionContext? session);

    void Remove(string token);

    int RemoveByRoom(Guid roomId);
}

public sealed record PlayerSessionContext(Guid PlayerId, Guid RoomId, bool IsHost);
