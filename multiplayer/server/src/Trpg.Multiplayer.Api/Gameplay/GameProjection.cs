namespace Trpg.Multiplayer.Api.Gameplay;

public sealed record CharacterSnapshot(
    Guid CharacterId,
    Guid OwnerPlayerId,
    string Name,
    IReadOnlyDictionary<string, int> CheckValues);

public sealed record GameSnapshot(
    Guid RoomId,
    long Revision,
    string Status,
    DateTimeOffset CreatedAt,
    IReadOnlyList<CharacterSnapshot> Characters);

public static class GameProjection
{
    public static GameSnapshot Build(MultiplayerGameState state, Guid viewerPlayerId)
    {
        _ = viewerPlayerId;
        return new GameSnapshot(
            state.RoomId,
            state.Revision,
            state.Status.ToString(),
            state.CreatedAt,
            state.Characters
                .Select(character => new CharacterSnapshot(
                    character.CharacterId,
                    character.OwnerPlayerId,
                    character.Name,
                    new Dictionary<string, int>(character.CheckValues, StringComparer.OrdinalIgnoreCase)))
                .ToArray());
    }
}
