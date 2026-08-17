namespace Trpg.Multiplayer.Api.Gameplay;

public interface IGameStateStore
{
    bool TryAdd(MultiplayerGameState state);

    bool TryGet(Guid roomId, out MultiplayerGameState? state);

    bool TryReplace(MultiplayerGameState expectedState, MultiplayerGameState replacementState);

    bool TryRemove(Guid roomId, out MultiplayerGameState? state);

    bool Exists(Guid roomId);
}
