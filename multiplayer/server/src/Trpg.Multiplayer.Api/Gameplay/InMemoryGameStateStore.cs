using System.Collections.Concurrent;

namespace Trpg.Multiplayer.Api.Gameplay;

public sealed class InMemoryGameStateStore : IGameStateStore
{
    private readonly ConcurrentDictionary<Guid, MultiplayerGameState> states = new();

    public bool TryAdd(MultiplayerGameState state) => states.TryAdd(state.RoomId, state);

    public bool TryGet(Guid roomId, out MultiplayerGameState? state) => states.TryGetValue(roomId, out state);

    public bool TryReplace(MultiplayerGameState expectedState, MultiplayerGameState replacementState) =>
        states.TryUpdate(replacementState.RoomId, replacementState, expectedState);

    public bool TryRemove(Guid roomId, out MultiplayerGameState? state) => states.TryRemove(roomId, out state);

    public bool Exists(Guid roomId) => states.ContainsKey(roomId);
}
